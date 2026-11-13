using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models.Entities;
using E_COMMERCE_FuentesFacundo.Models.ViewModels;
using E_COMMERCE_FuentesFacundo.Services;

namespace E_COMMERCE_FuentesFacundo.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class ReportesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PedidoReporteService _reporte;

        public ReportesController(ApplicationDbContext context, PedidoReporteService reporte)
        {
            _context = context;
            _reporte = reporte;
        }

        // GET: Reportes/Pedidos
        public async Task<IActionResult> Pedidos(DateTime? desde, DateTime? hasta, string? estado)
        {
            var pedidos = await ObtenerPedidosAsync(desde, hasta, estado);

            ViewBag.Estados = new SelectList(
                await _context.EstadosPedido.OrderBy(e => e.IdEstadoPedido).ToListAsync(),
                "Nombre",
                "Nombre",
                estado);

            return View(new ReportePedidosViewModel
            {
                Desde = desde,
                Hasta = hasta,
                Estado = estado,
                Pedidos = pedidos
            });
        }

        // GET: Reportes/DescargarPedidosPdf
        public async Task<IActionResult> DescargarPedidosPdf(DateTime? desde, DateTime? hasta, string? estado)
        {
            var pedidos = await ObtenerPedidosAsync(desde, hasta, estado);
            var pdf = _reporte.GenerarListadoPdf(pedidos, desde, hasta, estado);

            return File(pdf, "application/pdf", $"pedidos-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
        }

        private async Task<List<Pedido>> ObtenerPedidosAsync(DateTime? desde, DateTime? hasta, string? estado)
        {
            var query = _context.Pedidos
                .AsNoTracking()
                .Include(p => p.Usuario)
                .Include(p => p.EstadoPedido)
                .Include(p => p.MetodoEntrega)
                .Include(p => p.DetallesPedido)
                .Include(p => p.Pagos)
                .AsQueryable();

            if (desde.HasValue)
            {
                query = query.Where(p => p.Fecha >= desde.Value.Date);
            }

            if (hasta.HasValue)
            {
                var limite = hasta.Value.Date.AddDays(1);
                query = query.Where(p => p.Fecha < limite);
            }

            if (!string.IsNullOrWhiteSpace(estado))
            {
                query = query.Where(p => p.EstadoPedido != null && p.EstadoPedido.Nombre == estado);
            }

            return await query.OrderByDescending(p => p.Fecha).ToListAsync();
        }
    }
}
