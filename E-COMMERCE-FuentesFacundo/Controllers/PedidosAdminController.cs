using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models;
using E_COMMERCE_FuentesFacundo.Models.Entities;
using E_COMMERCE_FuentesFacundo.Services;

namespace E_COMMERCE_FuentesFacundo.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class PedidosAdminController : Controller
    {
        private static readonly Dictionary<string, string[]> Transiciones = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Pendiente"] = new[] { "Pagado", "Cancelado" },
            ["Pagado"] = new[] { "Enviado", "Cancelado" },
            ["Enviado"] = new[] { "Entregado" },
            ["Entregado"] = Array.Empty<string>(),
            ["Cancelado"] = Array.Empty<string>()
        };

        private readonly ApplicationDbContext _context;
        private readonly AuditoriaService _auditoria;

        public PedidosAdminController(ApplicationDbContext context, AuditoriaService auditoria)
        {
            _context = context;
            _auditoria = auditoria;
        }

        // GET: PedidosAdmin
        public async Task<IActionResult> Index(string? estado, string? q, int page = 1)
        {
            var query = _context.Pedidos
                .Include(p => p.Usuario)
                .Include(p => p.EstadoPedido)
                .Include(p => p.MetodoEntrega)
                .Include(p => p.DetallesPedido)
                .Include(p => p.Pagos)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(estado))
            {
                query = query.Where(p => p.EstadoPedido != null && p.EstadoPedido.Nombre == estado);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                var like = $"%{q}%";
                if (int.TryParse(q, out var idPedido))
                {
                    query = query.Where(p => p.IdPedido == idPedido
                        || (p.Usuario != null && (
                            EF.Functions.Like(p.Usuario.Nombre, like)
                            || EF.Functions.Like(p.Usuario.Apellido, like)
                            || (p.Usuario.Email != null && EF.Functions.Like(p.Usuario.Email, like)))));
                }
                else
                {
                    query = query.Where(p => p.Usuario != null && (
                        EF.Functions.Like(p.Usuario.Nombre, like)
                        || EF.Functions.Like(p.Usuario.Apellido, like)
                        || (p.Usuario.Email != null && EF.Functions.Like(p.Usuario.Email, like))));
                }
            }

            var pedidos = await PaginatedList<Pedido>.CreateAsync(
                query.OrderByDescending(p => p.Fecha),
                page,
                PaginatedList<Pedido>.DefaultPageSize);

            ViewData["Estado"] = estado;
            ViewData["q"] = q;
            ViewBag.Estados = new SelectList(
                await _context.EstadosPedido.OrderBy(e => e.IdEstadoPedido).ToListAsync(),
                "Nombre",
                "Nombre",
                estado);

            return View(pedidos);
        }

        // GET: PedidosAdmin/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var pedido = await _context.Pedidos
                .Include(p => p.Usuario)
                .Include(p => p.EstadoPedido)
                .Include(p => p.MetodoEntrega)
                .Include(p => p.DireccionEnvio)
                    .ThenInclude(d => d!.Provincia)
                .Include(p => p.DetallesPedido)
                    .ThenInclude(d => d.Producto)
                .Include(p => p.Pagos)
                .FirstOrDefaultAsync(p => p.IdPedido == id);

            if (pedido == null)
            {
                return NotFound();
            }

            var estadoActual = pedido.EstadoPedido?.Nombre ?? string.Empty;
            var nombres = ObtenerSiguientes(estadoActual);
            var estados = await _context.EstadosPedido.ToListAsync();

            ViewBag.EstadoActual = estadoActual;
            ViewBag.Siguientes = estados
                .Where(e => nombres.Contains(e.Nombre, StringComparer.OrdinalIgnoreCase))
                .OrderBy(e => Array.FindIndex(nombres, n => string.Equals(n, e.Nombre, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            return View(pedido);
        }

        // POST: PedidosAdmin/CambiarEstado/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(int id, int idEstado)
        {
            var pedido = await _context.Pedidos
                .Include(p => p.EstadoPedido)
                .Include(p => p.DetallesPedido)
                .FirstOrDefaultAsync(p => p.IdPedido == id);

            if (pedido == null)
            {
                return NotFound();
            }

            var nuevoEstado = await _context.EstadosPedido.FindAsync(idEstado);
            if (nuevoEstado == null)
            {
                TempData["Error"] = "El estado seleccionado no es válido.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var actual = pedido.EstadoPedido?.Nombre ?? string.Empty;
            var permitidos = ObtenerSiguientes(actual);
            if (!permitidos.Contains(nuevoEstado.Nombre, StringComparer.OrdinalIgnoreCase))
            {
                TempData["Error"] = $"No se puede pasar de '{actual}' a '{nuevoEstado.Nombre}'.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (string.Equals(nuevoEstado.Nombre, "Cancelado", StringComparison.OrdinalIgnoreCase))
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    await RestaurarStockAsync(pedido);
                    pedido.IdEstadoPedido = nuevoEstado.IdEstadoPedido;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }

                TempData["Success"] = "Pedido cancelado y stock repuesto.";
            }
            else
            {
                pedido.IdEstadoPedido = nuevoEstado.IdEstadoPedido;
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Estado del pedido actualizado a '{nuevoEstado.Nombre}'.";
            }

            await _auditoria.RegistrarAsync("CambiarEstado", "Pedido", id.ToString(),
                new { Estado = actual },
                new { Estado = nuevoEstado.Nombre });

            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: PedidosAdmin/ConfirmarPago/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarPago(int id)
        {
            var pedido = await _context.Pedidos
                .Include(p => p.EstadoPedido)
                .Include(p => p.Pagos)
                .FirstOrDefaultAsync(p => p.IdPedido == id);

            if (pedido == null)
            {
                return NotFound();
            }

            if (string.Equals(pedido.EstadoPedido?.Nombre, "Cancelado", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "No se puede confirmar el pago de un pedido cancelado.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var pago = pedido.Pagos.OrderByDescending(p => p.IdPago).FirstOrDefault();
            if (pago == null)
            {
                TempData["Error"] = "El pedido no tiene un pago registrado.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (string.Equals(pago.EstadoPago, "Aprobado", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Success"] = "El pago ya estaba aprobado.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var estadoAnterior = pedido.EstadoPedido?.Nombre ?? string.Empty;
            var estadoPagoAnterior = pago.EstadoPago;

            pago.EstadoPago = "Aprobado";
            pago.FechaPago = DateTime.Now;
            var estadoNuevo = estadoAnterior;

            if (string.Equals(pedido.EstadoPedido?.Nombre, "Pendiente", StringComparison.OrdinalIgnoreCase))
            {
                var pagado = await _context.EstadosPedido
                    .FirstOrDefaultAsync(e => e.Nombre == "Pagado");

                if (pagado == null)
                {
                    TempData["Error"] = "No se encontró el estado 'Pagado'.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                pedido.IdEstadoPedido = pagado.IdEstadoPedido;
                estadoNuevo = pagado.Nombre;
            }

            await _context.SaveChangesAsync();

            await _auditoria.RegistrarAsync("ConfirmarPago", "Pedido", id.ToString(),
                new { Estado = estadoAnterior, EstadoPago = estadoPagoAnterior },
                new { Estado = estadoNuevo, EstadoPago = pago.EstadoPago });

            TempData["Success"] = "Pago confirmado correctamente.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: PedidosAdmin/RechazarPago/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RechazarPago(int id)
        {
            var pedido = await _context.Pedidos
                .Include(p => p.EstadoPedido)
                .Include(p => p.Pagos)
                .Include(p => p.DetallesPedido)
                .FirstOrDefaultAsync(p => p.IdPedido == id);

            if (pedido == null)
            {
                return NotFound();
            }

            var actual = pedido.EstadoPedido?.Nombre ?? string.Empty;
            if (string.Equals(actual, "Cancelado", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "El pedido ya está cancelado.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (!string.Equals(actual, "Pendiente", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(actual, "Pagado", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Solo se puede rechazar el pago de pedidos Pendientes o Pagados.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var pago = pedido.Pagos.OrderByDescending(p => p.IdPago).FirstOrDefault();
            if (pago == null)
            {
                TempData["Error"] = "El pedido no tiene un pago registrado.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var cancelado = await _context.EstadosPedido
                .FirstOrDefaultAsync(e => e.Nombre == "Cancelado");
            if (cancelado == null)
            {
                TempData["Error"] = "No se encontró el estado 'Cancelado'.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var estadoPagoAnterior = pago.EstadoPago;

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                pago.EstadoPago = "Rechazado";
                pago.FechaPago = null;
                await RestaurarStockAsync(pedido);
                pedido.IdEstadoPedido = cancelado.IdEstadoPedido;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            await _auditoria.RegistrarAsync("RechazarPago", "Pedido", id.ToString(),
                new { Estado = actual, EstadoPago = estadoPagoAnterior },
                new { Estado = cancelado.Nombre, EstadoPago = pago.EstadoPago, StockRepuesto = true });

            TempData["Success"] = "Pago rechazado, pedido cancelado y stock repuesto.";
            return RedirectToAction(nameof(Details), new { id });
        }

        private static string[] ObtenerSiguientes(string estadoActual)
        {
            return Transiciones.TryGetValue(estadoActual, out var siguientes)
                ? siguientes
                : Array.Empty<string>();
        }

        private async Task RestaurarStockAsync(Pedido pedido)
        {
            var ids = pedido.DetallesPedido.Select(d => d.IdProducto).Distinct().ToList();
            if (!ids.Any())
            {
                return;
            }

            var productos = await _context.Productos
                .Where(p => ids.Contains(p.IdProducto))
                .ToDictionaryAsync(p => p.IdProducto);

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
