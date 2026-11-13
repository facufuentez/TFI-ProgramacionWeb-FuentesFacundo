using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace E_COMMERCE_FuentesFacundo.Tests;

public class AuthTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Login_Get_ReturnsOk()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidAdminCredentials_RedirectsToHome()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync("facu.fuentez@gmail.com");
        var adminInfo = admin is null
            ? "User not found"
            : $"User found; roles={string.Join(",", await userManager.GetRolesAsync(admin))}";

        var client = CreateClient();
        var loginPage = await client.GetAsync("/Account/Login");
        var token = TestHelpers.GetAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "facu.fuentez@gmail.com",
            ["Password"] = "facuadmin11",
            ["RememberMe"] = "false",
            ["__RequestVerificationToken"] = token!
        });

        var response = await client.PostAsync("/Account/Login", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.Redirect,
            $"Status={response.StatusCode}; Location={response.Headers.Location}; Admin={adminInfo}; Body={body}");
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsLoginPage()
    {
        var client = CreateClient();
        var loginPage = await client.GetAsync("/Account/Login");
        var token = TestHelpers.GetAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "facu.fuentez@gmail.com",
            ["Password"] = "clave-incorrecta",
            ["RememberMe"] = "false",
            ["__RequestVerificationToken"] = token!
        });

        var response = await client.PostAsync("/Account/Login", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("incorrectos", body);
    }

    [Fact]
    public async Task AnonymousUser_CannotAccessAdminProducts()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/Productos");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString ?? string.Empty);
    }

    [Fact]
    public async Task AuthenticatedClient_CannotAccessAdminPedidos()
    {
        var client = CreateClient();
        await LoginAsClientAsync(client);

        var response = await client.GetAsync("/PedidosAdmin");

        Assert.True(response.StatusCode == HttpStatusCode.Redirect ||
                    response.StatusCode == HttpStatusCode.Forbidden);
    }

    private async Task LoginAsClientAsync(HttpClient client)
    {
        using var scope = _factory.Services.CreateScope();
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

        var loginPage = await client.GetAsync("/Account/Login");
        var token = TestHelpers.GetAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = "cliente123",
            ["RememberMe"] = "false",
            ["__RequestVerificationToken"] = token!
        });

        await client.PostAsync("/Account/Login", content);
    }
}
