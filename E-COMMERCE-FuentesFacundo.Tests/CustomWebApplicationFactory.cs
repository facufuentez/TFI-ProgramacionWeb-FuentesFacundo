using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models.Identity;
using E_COMMERCE_FuentesFacundo.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace E_COMMERCE_FuentesFacundo.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var databaseDescriptors = services
                .Where(d =>
                    d.ServiceType == typeof(ApplicationDbContext) ||
                    (d.ServiceType.FullName != null &&
                     (d.ServiceType.FullName.Contains("SqlServer", StringComparison.Ordinal) ||
                      d.ServiceType.FullName.Contains("EntityFrameworkCore", StringComparison.Ordinal) ||
                      d.ServiceType.FullName.Contains("DbContextOptions", StringComparison.Ordinal))))
                .ToList();

            foreach (var descriptor in databaseDescriptors)
            {
                services.Remove(descriptor);
            }

            var databaseName = "EcommerceTests-" + Guid.NewGuid();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(databaseName)
                    .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

            var testConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MercadoPago:AccessToken"] = "test-token",
                    ["MercadoPago:WebhookSecret"] = "test-secret",
                    ["MercadoPago:BaseUrl"] = "https://api.mercadopago.com/"
                })
                .Build();

            var configurationDescriptors = services
                .Where(d => d.ServiceType == typeof(IConfiguration))
                .ToList();

            foreach (var descriptor in configurationDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IConfiguration>(testConfiguration);

            var mercadoPagoDescriptors = services
                .Where(d => d.ServiceType == typeof(MercadoPagoService))
                .ToList();

            foreach (var descriptor in mercadoPagoDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddScoped<MercadoPagoService>(sp => new FakeMercadoPagoService(
                sp.GetRequiredService<ApplicationDbContext>(),
                sp.GetRequiredService<AuditoriaService>(),
                sp.GetRequiredService<ILogger<MercadoPagoService>>()));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        const string adminRole = "Administrador";
        const string adminEmail = "facu.fuentez@gmail.com";
        const string adminPassword = "facuadmin11";

        if (!roleManager.RoleExistsAsync(adminRole).GetAwaiter().GetResult())
        {
            roleManager.CreateAsync(new IdentityRole(adminRole)).GetAwaiter().GetResult();
        }

        var admin = userManager.FindByEmailAsync(adminEmail).GetAwaiter().GetResult();
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                Nombre = "Facundo",
                Apellido = "Fuentes",
                FechaNacimiento = new DateTime(2004, 5, 15),
                EmailConfirmed = true
            };

            userManager.CreateAsync(admin, adminPassword).GetAwaiter().GetResult();
        }

        if (!userManager.IsInRoleAsync(admin, adminRole).GetAwaiter().GetResult())
        {
            userManager.AddToRoleAsync(admin, adminRole).GetAwaiter().GetResult();
        }

        return host;
    }
}
