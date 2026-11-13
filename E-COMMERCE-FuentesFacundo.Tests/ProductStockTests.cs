using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models.Entities;
using E_COMMERCE_FuentesFacundo.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace E_COMMERCE_FuentesFacundo.Tests;

public class ProductStockTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ProductStockTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task CreateProduct_WithNegativePriceOrStock_ShowsValidation()
    {
        await LoginAsAdminAsync();

        var createPage = await _client.GetAsync("/Productos/Create");
        var token = TestHelpers.GetAntiforgeryToken(await createPage.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = new Categoria { Nombre = $"Categoria-{Guid.NewGuid():N}" };
        context.Categorias.Add(category);
        await context.SaveChangesAsync();

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["IdCategoria"] = category.IdCategoria.ToString(),
            ["Nombre"] = "Producto Test",
            ["Descripcion"] = "Descripcion",
            ["Precio"] = "-10",
            ["Stock"] = "-5",
            ["Activo"] = "true",
            ["__RequestVerificationToken"] = token!
        });

        var response = await _client.PostAsync("/Productos/Create", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no puede ser negativo", body);
    }

    [Fact]
    public async Task Checkout_WithEfectivo_DecrementsStock()
    {
        var (clientEmail, productId) = await SeedClientAndProductAsync(stock: 5);

        await LoginAsync(clientEmail, "cliente123");

        var cartPage = await _client.GetAsync("/Carrito");
        var cartToken = TestHelpers.GetAntiforgeryToken(await cartPage.Content.ReadAsStringAsync());

        var addContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["idProducto"] = productId.ToString(),
            ["cantidad"] = "2",
            ["returnUrl"] = "/",
            ["__RequestVerificationToken"] = cartToken!
        });

        await _client.PostAsync("/Carrito/Add", addContent);

        var checkoutPage = await _client.GetAsync("/Carrito/Checkout");
        var checkoutToken = TestHelpers.GetAntiforgeryToken(await checkoutPage.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await context.Users.Where(u => u.Email == clientEmail).Select(u => u.Id).FirstAsync();
        var addressId = await context.Direcciones.Where(d => d.IdUsuario == userId).Select(d => d.IdDireccion).FirstAsync();
        var shippingId = await context.MetodosEntrega.Select(m => m.IdMetodoEntrega).FirstAsync();

        var checkoutContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["IdDireccion"] = addressId.ToString(),
            ["IdMetodoEntrega"] = shippingId.ToString(),
            ["MetodoPago"] = "Efectivo",
            ["__RequestVerificationToken"] = checkoutToken!
        });

        var checkoutResponse = await _client.PostAsync("/Carrito/Checkout", checkoutContent);

        Assert.Equal(HttpStatusCode.Redirect, checkoutResponse.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var product = await verifyContext.Productos.FindAsync(productId);
        Assert.NotNull(product);
        Assert.Equal(3, product!.Stock);
    }

    private async Task LoginAsAdminAsync()
    {
        var loginPage = await _client.GetAsync("/Account/Login");
        var token = TestHelpers.GetAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "facu.fuentez@gmail.com",
            ["Password"] = "facuadmin11",
            ["RememberMe"] = "false",
            ["__RequestVerificationToken"] = token!
        });

        await _client.PostAsync("/Account/Login", content);
    }

    private async Task LoginAsync(string email, string password)
    {
        var loginPage = await _client.GetAsync("/Account/Login");
        var token = TestHelpers.GetAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false",
            ["__RequestVerificationToken"] = token!
        });

        await _client.PostAsync("/Account/Login", content);
    }

    private async Task<(string Email, int ProductId)> SeedClientAndProductAsync(int stock)
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

        context.Direcciones.Add(new Direccion
        {
            IdUsuario = user.Id,
            IdProvincia = provinciaId,
            Calle = "Calle Test",
            Numero = "123",
            Ciudad = "CABA",
            CodigoPostal = "1000",
            Activa = true
        });

        if (!await context.MetodosEntrega.AnyAsync())
        {
            context.MetodosEntrega.Add(new MetodoEntrega { Nombre = "Envio Test", Costo = 1000 });
            await context.SaveChangesAsync();
        }

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

        return (email, product.IdProducto);
    }
}
