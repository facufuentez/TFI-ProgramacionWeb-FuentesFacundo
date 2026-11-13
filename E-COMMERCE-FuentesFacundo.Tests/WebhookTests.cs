using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models.Entities;
using E_COMMERCE_FuentesFacundo.Models.Identity;
using E_COMMERCE_FuentesFacundo.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace E_COMMERCE_FuentesFacundo.Tests;

public class WebhookTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private const string Secret = "test-secret";

    public WebhookTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Webhook_WithValidSignature_ConvertsCheckoutAndCreatesOrder()
    {
        var (checkoutId, productId, userId) = await SeedCheckoutAsync(stock: 5);

        var (signature, requestId) = CreateSignature(checkoutId.ToString());

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/mercadopago/webhook?data.id={checkoutId}")
        {
            Content = new StringContent("{\"type\":\"order\",\"data\":{\"id\":\"" + checkoutId + "\"}}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-signature", signature);
        request.Headers.Add("x-request-id", requestId);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == System.Net.HttpStatusCode.OK, $"Status={response.StatusCode}; Body={body}");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var checkout = await context.MercadoPagoCheckouts.FindAsync(checkoutId);
        Assert.NotNull(checkout);
        Assert.Equal("Convertido", checkout!.Estado);
        Assert.True(checkout.IdPedido.HasValue);

        var pedido = await context.Pedidos
            .Include(p => p.EstadoPedido)
            .Include(p => p.Pagos)
            .FirstOrDefaultAsync(p => p.IdPedido == checkout.IdPedido.Value);
        Assert.NotNull(pedido);
        Assert.Equal("Pagado", pedido!.EstadoPedido?.Nombre);
        Assert.Contains(pedido.Pagos, p => p.EstadoPago == "Aprobado" && p.IdMercadoPago == checkoutId.ToString());

        var product = await context.Productos.FindAsync(productId);
        Assert.NotNull(product);
        Assert.Equal(3, product!.Stock);
    }

    [Fact]
    public async Task Webhook_WithInvalidSignature_ReturnsUnauthorized()
    {
        var (checkoutId, _, _) = await SeedCheckoutAsync(stock: 5);

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/mercadopago/webhook?data.id={checkoutId}")
        {
            Content = new StringContent("{\"type\":\"order\",\"data\":{\"id\":\"" + checkoutId + "\"}}", Encoding.UTF8, "application/json")
        };
        invalidRequest.Headers.Add("x-signature", "ts=1,v1=invalid");
        invalidRequest.Headers.Add("x-request-id", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(invalidRequest);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(Guid CheckoutId, int ProductId, string UserId)> SeedCheckoutAsync(int stock)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roleManager.RoleExistsAsync("Cliente"))
        {
            await roleManager.CreateAsync(new IdentityRole("Cliente"));
        }

        var email = $"cliente-{Guid.NewGuid():N}@test.com";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            Nombre = "Cliente",
            Apellido = "Test",
            FechaNacimiento = new DateTime(2000, 1, 1),
            EmailConfirmed = true
        };

        await userManager.CreateAsync(user, "cliente123");
        await userManager.AddToRoleAsync(user, "Cliente");

        if (!await context.Provincias.AnyAsync())
        {
            context.Provincias.Add(new Provincia { Nombre = "Buenos Aires" });
            await context.SaveChangesAsync();
        }

        var provinciaId = await context.Provincias.Select(p => p.IdProvincia).FirstAsync();

        var address = new Direccion
        {
            IdUsuario = user.Id,
            IdProvincia = provinciaId,
            Calle = "Calle Test",
            Numero = "123",
            Ciudad = "CABA",
            CodigoPostal = "1000",
            Activa = true
        };
        context.Direcciones.Add(address);

        if (!await context.MetodosEntrega.AnyAsync())
        {
            context.MetodosEntrega.Add(new MetodoEntrega { Nombre = "Envio Test", Costo = 1000 });
            await context.SaveChangesAsync();
        }

        var shippingId = await context.MetodosEntrega.Select(m => m.IdMetodoEntrega).FirstAsync();

        var category = new Categoria { Nombre = $"Categoria-{Guid.NewGuid():N}" };
        context.Categorias.Add(category);
        await context.SaveChangesAsync();

        var product = new Producto
        {
            IdCategoria = category.IdCategoria,
            Nombre = $"Producto-{Guid.NewGuid():N}",
            Descripcion = "Producto de prueba",
            Precio = 10000,
            Stock = stock,
            Activo = true
        };
        context.Productos.Add(product);
        await context.SaveChangesAsync();

        var items = new List<MercadoPagoCheckoutItem>
        {
            new(product.IdProducto, product.Nombre, product.Precio, 2)
        };

        var checkout = new MercadoPagoCheckout
        {
            IdUsuario = user.Id,
            IdDireccionEnvio = address.IdDireccion,
            IdMetodoEntrega = shippingId,
            Total = 20000 + 1000,
            CostoEnvio = 1000,
            ItemsJson = JsonSerializer.Serialize(items),
            IdMercadoPago = null,
            Estado = "Pendiente",
            Fecha = DateTime.Now
        };

        checkout.IdMercadoPago = checkout.Id.ToString();
        context.MercadoPagoCheckouts.Add(checkout);
        await context.SaveChangesAsync();

        return (checkout.Id, product.IdProducto, user.Id);
    }

    private static (string Signature, string RequestId) CreateSignature(string dataId)
    {
        var requestId = Guid.NewGuid().ToString();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var manifest = $"id:{dataId};request-id:{requestId};ts:{timestamp};";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();

        return ($"ts={timestamp},v1={hash}", requestId);
    }
}
