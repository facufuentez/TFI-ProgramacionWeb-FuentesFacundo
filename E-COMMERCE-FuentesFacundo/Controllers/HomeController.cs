using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models;
using E_COMMERCE_FuentesFacundo.Models.Entities;
using E_COMMERCE_FuentesFacundo.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace E_COMMERCE_FuentesFacundo.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;

        public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<IActionResult> Index(string? q, int page = 1)
        {
            q = q?.Trim();
            if (q != null && q.Length > 100)
            {
                q = q[..100];
            }

            var query = _context.Productos
                .Include(p => p.Categoria)
                .AsNoTracking()
                .Where(p => p.Activo);

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(p => p.Nombre.Contains(q)
                    || (p.Descripcion != null && p.Descripcion.Contains(q))
                    || (p.Categoria != null && p.Categoria.Nombre.Contains(q)));
            }

            var productos = await PaginatedList<Producto>.CreateAsync(
                query.OrderBy(p => p.Nombre),
                page,
                PaginatedList<Producto>.DefaultPageSize);

            var carruselProductos = new List<Producto>();
            if (string.IsNullOrWhiteSpace(q) && page == 1)
            {
                var candidatos = await _context.Productos
                    .AsNoTracking()
                    .Include(p => p.Categoria)
                    .Where(p => p.Activo && !string.IsNullOrEmpty(p.ImagenUrl))
                    .ToListAsync();

                carruselProductos = candidatos
                    .OrderBy(_ => Random.Shared.Next())
                    .Take(5)
                    .ToList();
            }

            return View(new HomeIndexViewModel
            {
                Busqueda = q,
                Productos = productos,
                CarruselProductos = carruselProductos
            });
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
