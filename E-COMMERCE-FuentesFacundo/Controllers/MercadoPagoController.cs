using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models.Entities;
using E_COMMERCE_FuentesFacundo.Models.Identity;
using E_COMMERCE_FuentesFacundo.Services;

namespace E_COMMERCE_FuentesFacundo.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MercadoPagoController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly MercadoPagoService _mercadoPago;
        private readonly AuditoriaService _auditoria;
        private readonly CartService _cart;
        private readonly ILogger<MercadoPagoController> _logger;
        private readonly IConfiguration _configuration;

        public MercadoPagoController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            MercadoPagoService mercadoPago,
            AuditoriaService auditoria,
            CartService cart,
            ILogger<MercadoPagoController> logger,
            IConfiguration configuration)
        {
            _context = context;
            _userManager = userManager;
            _mercadoPago = mercadoPago;
            _auditoria = auditoria;
            _cart = cart;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet("success")]
        public async Task<IActionResult> Success(
            string? external_reference,
            string? order_id,
            string? status,
            CancellationToken cancellationToken)
        {
            return await ProcesarResultadoAsync(external_reference, order_id, status, "Pago aprobado por Mercado Pago.", cancellationToken);
        }

        [HttpGet("failure")]
        public async Task<IActionResult> Failure(
            string? external_reference,
            string? order_id,
            string? status,
            CancellationToken cancellationToken)
        {
            return await ProcesarResultadoAsync(external_reference, order_id, status, "El pago fue rechazado o cancelado.", cancellationToken);
        }

        [HttpGet("pending")]
        public async Task<IActionResult> Pending(
            string? external_reference,
            string? order_id,
            string? status,
            CancellationToken cancellationToken)
        {
            return await ProcesarResultadoAsync(external_reference, order_id, status, "El pago quedó pendiente de acreditación.", cancellationToken);
        }

        [HttpGet("checkout/{checkoutId:guid}/estado")]
        [Authorize]
        public async Task<IActionResult> EstadoCheckout(Guid checkoutId, CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            var checkout = await _context.MercadoPagoCheckouts
                .FirstOrDefaultAsync(c => c.Id == checkoutId && c.IdUsuario == user.Id, cancellationToken);

            if (checkout is null)
            {
                return NotFound();
            }

            MercadoPagoOrderDetails? details = null;
            if (!string.IsNullOrWhiteSpace(checkout.IdMercadoPago))
            {
                try
                {
                    details = await _mercadoPago.ObtenerOrderAsync(checkout.IdMercadoPago, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return NoContent();
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "Error de red consultando la orden {OrderId} en Mercado Pago.", checkout.IdMercadoPago);
                }
                catch (MercadoPagoException ex)
                {
                    _logger.LogWarning(ex, "No se pudo consultar la orden {OrderId} en Mercado Pago.", checkout.IdMercadoPago);
                }
            }

            Pedido? pedido = null;
            if (details is not null)
            {
                if (details.PaymentStatus == "approved" || details.Status == "processed")
                {
                    try
                    {
                        pedido = await _mercadoPago.ConvertirCheckoutSiAprobadoAsync(checkout, details, cancellationToken);
                    }
                    catch (MercadoPagoException ex)
                    {
                        _logger.LogWarning(ex, "No se pudo convertir el checkout {CheckoutId}.", checkout.Id);
                        checkout.Estado = "Fallido";
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                }
                else
                {
                    await _mercadoPago.ActualizarEstadoCheckoutAsync(checkout, details, cancellationToken);
                }
            }

            if (pedido is null && checkout.IdPedido.HasValue)
            {
                pedido = await _context.Pedidos
                    .Include(p => p.EstadoPedido)
                    .FirstOrDefaultAsync(p => p.IdPedido == checkout.IdPedido.Value, cancellationToken);
            }

            if (pedido is not null && checkout.Estado == "Convertido")
            {
                _cart.Clear();
            }

            return Ok(new
            {
                estadoPago = checkout.Estado,
                pedidoId = checkout.IdPedido,
                mpStatus = details?.Status,
                mpPaymentStatus = details?.PaymentStatus
            });
        }

        [HttpGet("estado/{pedidoId:int}")]
        [Authorize]
        public async Task<IActionResult> Estado(int pedidoId, CancellationToken cancellationToken)
        {
            var pedido = await _context.Pedidos
                .Include(p => p.EstadoPedido)
                .Include(p => p.Pagos)
                .FirstOrDefaultAsync(p => p.IdPedido == pedidoId, cancellationToken);

            if (pedido is null)
            {
                return NotFound();
            }

            var user = await _userManager.GetUserAsync(User);
            var esAdmin = User.IsInRole("Administrador");
            if (user is null || (!esAdmin && pedido.IdUsuario != user.Id))
            {
                return NotFound();
            }

            var pago = pedido.Pagos
                .OrderByDescending(p => p.IdPago)
                .FirstOrDefault(p => p.MetodoPago == "Mercado Pago")
                ?? pedido.Pagos.OrderByDescending(p => p.IdPago).FirstOrDefault();

            if (pago is null)
            {
                return NotFound();
            }

            MercadoPagoOrderDetails? details = null;
            if (!string.IsNullOrWhiteSpace(pago.IdMercadoPago))
            {
                try
                {
                    details = await _mercadoPago.ObtenerOrderAsync(pago.IdMercadoPago, cancellationToken);
                }
                catch (MercadoPagoException ex)
                {
                    _logger.LogWarning(ex, "No se pudo consultar la orden {OrderId} en Mercado Pago.", pago.IdMercadoPago);
                }
            }

            await SincronizarPagoAsync(pedido, pago, details, null, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                estadoPago = pago.EstadoPago,
                estadoPedido = pedido.EstadoPedido?.Nombre,
                mpStatus = details?.Status,
                mpPaymentStatus = details?.PaymentStatus
            });
        }

        [HttpPost("webhook")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
        {
            string body;
            using (var reader = new StreamReader(Request.Body))
            {
                body = await reader.ReadToEndAsync(cancellationToken);
            }

            string? dataId = Request.Query["data.id"].ToString();
            string? externalReference = null;

            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    using var document = JsonDocument.Parse(body);
                    if (document.RootElement.TryGetProperty("data", out var data) &&
                        data.TryGetProperty("id", out var idProperty))
                    {
                        dataId ??= idProperty.GetString();
                    }

                    if (document.RootElement.TryGetProperty("data", out var dataReference) &&
                        dataReference.TryGetProperty("external_reference", out var referenceProperty))
                    {
                        externalReference = referenceProperty.GetString();
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Webhook de Mercado Pago con JSON inválido.");
                    return BadRequest();
                }
            }

            if (string.IsNullOrWhiteSpace(dataId))
            {
                return Ok();
            }

            var secret = _configuration["MercadoPago:WebhookSecret"];
            if (string.IsNullOrWhiteSpace(secret))
            {
                _logger.LogWarning("Webhook de Mercado Pago recibido sin secreto configurado.");
                return Unauthorized();
            }

            var xSignature = Request.Headers["x-signature"].ToString();
            var xRequestId = Request.Headers["x-request-id"].ToString();

            if (!MercadoPagoService.ValidarFirmaWebhook(xSignature, xRequestId, dataId, secret))
            {
                _logger.LogWarning("Firma inválida en webhook de Mercado Pago.");
                return Unauthorized();
            }

            var checkout = await _context.MercadoPagoCheckouts
                .FirstOrDefaultAsync(c => c.IdMercadoPago == dataId, cancellationToken);

            if (checkout is null && Guid.TryParse(externalReference, out var checkoutId))
            {
                checkout = await _context.MercadoPagoCheckouts
                    .FirstOrDefaultAsync(c => c.Id == checkoutId, cancellationToken);

                if (checkout is not null && string.IsNullOrWhiteSpace(checkout.IdMercadoPago))
                {
                    checkout.IdMercadoPago = dataId;
                }
            }

            if (checkout is null)
            {
                _logger.LogInformation("Webhook de Mercado Pago para checkout desconocido: {OrderId}", dataId);
                return Ok();
            }

            MercadoPagoOrderDetails details;
            try
            {
                details = await _mercadoPago.ObtenerOrderAsync(dataId, cancellationToken);
            }
            catch (MercadoPagoException ex)
            {
                _logger.LogWarning(ex, "No se pudo consultar la orden {OrderId} en Mercado Pago.", dataId);
                return Ok();
            }

            if (details.PaymentStatus == "approved" || details.Status == "processed")
            {
                try
                {
                    await _mercadoPago.ConvertirCheckoutSiAprobadoAsync(checkout, details, cancellationToken);
                }
                catch (MercadoPagoException ex)
                {
                    _logger.LogWarning(ex, "No se pudo convertir el checkout {CheckoutId}.", checkout.Id);
                    checkout.Estado = "Fallido";
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }
            else
            {
                await _mercadoPago.ActualizarEstadoCheckoutAsync(checkout, details, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
            return Ok();
        }

        private async Task<IActionResult> ProcesarResultadoAsync(
            string? externalReference,
            string? orderId,
            string? statusHint,
            string mensaje,
            CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            MercadoPagoCheckout? checkout = null;

            if (Guid.TryParse(externalReference, out var checkoutId))
            {
                checkout = await _context.MercadoPagoCheckouts
                    .FirstOrDefaultAsync(c => c.Id == checkoutId && c.IdUsuario == user.Id, cancellationToken);
            }

            if (checkout is null && !string.IsNullOrWhiteSpace(orderId))
            {
                checkout = await _context.MercadoPagoCheckouts
                    .FirstOrDefaultAsync(c => c.IdMercadoPago == orderId && c.IdUsuario == user.Id, cancellationToken);
            }

            if (checkout is null)
            {
                return NotFound();
            }

            if (!string.IsNullOrWhiteSpace(orderId) && string.IsNullOrWhiteSpace(checkout.IdMercadoPago))
            {
                checkout.IdMercadoPago = orderId;
            }

            MercadoPagoOrderDetails? details = null;
            var mpOrderId = checkout.IdMercadoPago ?? orderId;
            if (!string.IsNullOrWhiteSpace(mpOrderId))
            {
                try
                {
                    details = await _mercadoPago.ObtenerOrderAsync(mpOrderId, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return NoContent();
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "Error de red consultando la orden {OrderId} en Mercado Pago.", mpOrderId);
                }
                catch (MercadoPagoException ex)
                {
                    _logger.LogWarning(ex, "No se pudo consultar la orden {OrderId} en Mercado Pago.", mpOrderId);
                }
            }

            if (details is not null)
            {
                if (details.PaymentStatus == "approved" || details.Status == "processed")
                {
                    try
                    {
                        await _mercadoPago.ConvertirCheckoutSiAprobadoAsync(checkout, details, cancellationToken);
                    }
                    catch (MercadoPagoException ex)
                    {
                        _logger.LogWarning(ex, "No se pudo convertir el checkout {CheckoutId}.", checkout.Id);
                        checkout.Estado = "Fallido";
                    }
                }
                else
                {
                    await _mercadoPago.ActualizarEstadoCheckoutAsync(checkout, details, cancellationToken);
                }
            }
            else if (statusHint is "rejected" or "pending")
            {
                checkout.Estado = statusHint == "rejected" ? "Rechazado" : "Pendiente";
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (checkout.IdPedido.HasValue)
            {
                TempData["Success"] = mensaje;
                return RedirectToAction("Details", "Pedidos", new { id = checkout.IdPedido.Value });
            }

            if (checkout.Estado is "Rechazado" or "Cancelado" or "Reembolsado" or "Fallido" or "Expirado")
            {
                TempData["Error"] = "El pago no fue aprobado. El carrito sigue disponible.";
            }
            else
            {
                TempData["Success"] = mensaje;
            }

            return RedirectToAction("Index", "Carrito");
        }

        private async Task SincronizarPagoAsync(
            Pedido pedido,
            Pago pago,
            MercadoPagoOrderDetails? details,
            string? statusHint,
            CancellationToken cancellationToken)
        {
            var estadoPagoAnterior = pago.EstadoPago;
            var estadoPedidoAnterior = pedido.EstadoPedido?.Nombre;

            string nuevoEstadoPago;
            string? nuevoEstadoPedido = null;
            bool reponerStock = false;

            if (details is not null)
            {
                var paymentStatus = details.PaymentStatus;
                var orderStatus = details.Status;

                if (paymentStatus == "approved" || orderStatus == "processed")
                {
                    nuevoEstadoPago = "Aprobado";
                }
                else if (paymentStatus == "rejected" || orderStatus == "failed")
                {
                    nuevoEstadoPago = "Rechazado";
                }
                else if (orderStatus == "canceled")
                {
                    nuevoEstadoPago = "Cancelado";
                }
                else if (paymentStatus == "refunded" || orderStatus == "refunded")
                {
                    nuevoEstadoPago = "Reembolsado";
                }
                else
                {
                    nuevoEstadoPago = "Pendiente";
                }
            }
            else
            {
                nuevoEstadoPago = statusHint switch
                {
                    "approved" => "Aprobado",
                    "rejected" => "Rechazado",
                    "pending" => "Pendiente",
                    _ => pago.EstadoPago
                };
            }

            if (nuevoEstadoPago == "Aprobado")
            {
                pago.EstadoPago = "Aprobado";
                pago.FechaPago ??= DateTime.Now;

                if (pedido.EstadoPedido?.Nombre == "Pendiente")
                {
                    var estadoPagado = await _context.EstadosPedido
                        .FirstOrDefaultAsync(e => e.Nombre == "Pagado", cancellationToken);
                    if (estadoPagado is not null)
                    {
                        pedido.IdEstadoPedido = estadoPagado.IdEstadoPedido;
                        nuevoEstadoPedido = estadoPagado.Nombre;
                    }
                }
            }
            else if (nuevoEstadoPago is "Rechazado" or "Cancelado")
            {
                pago.EstadoPago = nuevoEstadoPago;
                pago.FechaPago = null;

                if (pedido.EstadoPedido?.Nombre != "Cancelado" &&
                    estadoPagoAnterior is not ("Rechazado" or "Cancelado" or "Reembolsado"))
                {
                    var estadoCancelado = await _context.EstadosPedido
                        .FirstOrDefaultAsync(e => e.Nombre == "Cancelado", cancellationToken);
                    if (estadoCancelado is not null)
                    {
                        pedido.IdEstadoPedido = estadoCancelado.IdEstadoPedido;
                        nuevoEstadoPedido = estadoCancelado.Nombre;
                        reponerStock = true;
                    }
                }
            }
            else if (nuevoEstadoPago == "Reembolsado")
            {
                pago.EstadoPago = "Reembolsado";
                pago.FechaPago = null;

                if (pedido.EstadoPedido?.Nombre != "Cancelado" &&
                    estadoPagoAnterior is not ("Reembolsado" or "Cancelado"))
                {
                    var estadoCancelado = await _context.EstadosPedido
                        .FirstOrDefaultAsync(e => e.Nombre == "Cancelado", cancellationToken);
                    if (estadoCancelado is not null)
                    {
                        pedido.IdEstadoPedido = estadoCancelado.IdEstadoPedido;
                        nuevoEstadoPedido = estadoCancelado.Nombre;
                        reponerStock = true;
                    }
                }
            }
            else
            {
                pago.EstadoPago = "Pendiente";
                pago.FechaPago = null;
            }

            if (reponerStock)
            {
                await RestaurarStockAsync(pedido, cancellationToken);
            }

            if (estadoPagoAnterior != pago.EstadoPago || estadoPedidoAnterior != nuevoEstadoPedido)
            {
                await _auditoria.RegistrarAsync("SincronizarMercadoPago", "Pedido", pedido.IdPedido.ToString(),
                    new { EstadoPago = estadoPagoAnterior, EstadoPedido = estadoPedidoAnterior },
                    new { EstadoPago = pago.EstadoPago, EstadoPedido = nuevoEstadoPedido ?? pedido.EstadoPedido?.Nombre });
            }
        }

        private async Task RestaurarStockAsync(Pedido pedido, CancellationToken cancellationToken)
        {
            var ids = pedido.DetallesPedido.Select(d => d.IdProducto).Distinct().ToList();
            if (ids.Count == 0)
            {
                return;
            }

            var productos = await _context.Productos
                .Where(p => ids.Contains(p.IdProducto))
                .ToDictionaryAsync(p => p.IdProducto, cancellationToken);

            foreach (var detalle in pedido.DetallesPedido)
            {
                if (productos.TryGetValue(detalle.IdProducto, out var producto))
                {
                    producto.Stock += detalle.Cantidad;
                }
            }
        }
    }
}
