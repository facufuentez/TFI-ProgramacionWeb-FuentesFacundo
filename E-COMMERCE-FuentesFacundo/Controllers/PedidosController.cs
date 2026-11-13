using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models.Identity;
using E_COMMERCE_FuentesFacundo.Services;

namespace E_COMMERCE_FuentesFacundo.Controllers
{
    [Authorize]
    public class PedidosController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PedidoReporteService _reporte;

        public PedidosController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            PedidoReporteService reporte)
        {
            _context = context;
            _userManager = userManager;
            _reporte = reporte;
        }

        // GET: Pedidos
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var pedidos = await _context.Pedidos
                .Include(p => p.EstadoPedido)
                .Include(p => p.MetodoEntrega)
                .Include(p => p.DetallesPedido)
                .Where(p => p.IdUsuario == user.Id)
                .OrderByDescending(p => p.Fecha)
                .ToListAsync();

            return View(pedidos);
        }

        // GET: Pedidos/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var pedido = await _context.Pedidos
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

            var user = await _userManager.GetUserAsync(User);
            var esAdmin = User.IsInRole("Administrador");
            if (!esAdmin && (user == null || pedido.IdUsuario != user.Id))
            {
                return NotFound();
            }

            return View(pedido);
        }

        // GET: Pedidos/Comprobante/5
        public async Task<IActionResult> Comprobante(int? id)
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

            var user = await _userManager.GetUserAsync(User);
            var esAdmin = User.IsInRole("Administrador");
            if (!esAdmin && (user == null || pedido.IdUsuario != user.Id))
            {
                return NotFound();
            }

            var pdf = _reporte.GenerarComprobantePdf(pedido);
            return File(pdf, "application/pdf", $"comprobante-pedido-{pedido.IdPedido}.pdf");
        }
    }
}
