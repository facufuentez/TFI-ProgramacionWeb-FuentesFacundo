using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models.Entities;
using E_COMMERCE_FuentesFacundo.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace E_COMMERCE_FuentesFacundo.Tests;

public sealed class FakeMercadoPagoService : MercadoPagoService
{
    public FakeMercadoPagoService(
        ApplicationDbContext context,
        AuditoriaService auditoria,
        ILogger<MercadoPagoService> logger)
        : base(
            new HttpClient(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MercadoPago:AccessToken"] = "test-token",
                ["MercadoPago:BaseUrl"] = "https://api.mercadopago.com/"
            }).Build(),
            context,
            auditoria,
            logger)
    {
    }

    public override Task<MercadoPagoOrderResult> CrearOrderAsync(
        MercadoPagoCheckout checkout,
        string userEmail,
        string successUrl,
        string failureUrl,
        string pendingUrl,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new MercadoPagoOrderResult("MP-TEST-ORDER", "https://www.mercadopago.com.ar/checkout/test"));
    }

    public override Task<MercadoPagoOrderDetails> ObtenerOrderAsync(string orderId, CancellationToken cancellationToken)
    {
        return Task.FromResult(new MercadoPagoOrderDetails("processed", "accredited", "approved"));
    }
}
