using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models.Entities;
using E_COMMERCE_FuentesFacundo.Models.ViewModels;
using E_COMMERCE_FuentesFacundo.Services;

namespace E_COMMERCE_FuentesFacundo.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class MetodosEntregaController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditoriaService _auditoria;

        public MetodosEntregaController(ApplicationDbContext context, AuditoriaService auditoria)
        {
            _context = context;
            _auditoria = auditoria;
        }

        // GET: MetodosEntrega
        public async Task<IActionResult> Index()
        {
            var metodos = await _context.MetodosEntrega
                .Include(m => m.Pedidos)
                .ToListAsync();
            return View(metodos);
        }

        // GET: MetodosEntrega/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var metodo = await _context.MetodosEntrega
                .Include(m => m.Pedidos)
                .FirstOrDefaultAsync(m => m.IdMetodoEntrega == id);
            if (metodo == null)
            {
                return NotFound();
            }

            return View(metodo);
        }

        // GET: MetodosEntrega/Create
        public IActionResult Create()
        {
            return View(new MetodoEntregaViewModel());
        }

        // POST: MetodosEntrega/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MetodoEntregaViewModel model)
        {
            if (await _context.MetodosEntrega.AnyAsync(m => m.Nombre == model.Nombre))
            {
                ModelState.AddModelError(nameof(MetodoEntregaViewModel.Nombre), "Ya existe un método de entrega con ese nombre.");
            }

            if (ModelState.IsValid)
            {
                var metodo = new MetodoEntrega
                {
                    Nombre = model.Nombre,
                    Costo = model.Costo
                };

                _context.Add(metodo);
                await _context.SaveChangesAsync();

                await _auditoria.RegistrarAsync("Crear", "MetodoEntrega", metodo.IdMetodoEntrega.ToString(), null, new
                {
                    metodo.Nombre,
                    metodo.Costo
                });

                TempData["Success"] = "Método de entrega creado correctamente.";
                return RedirectToAction(nameof(Index));
            }

            return View(model);
        }

        // GET: MetodosEntrega/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var metodo = await _context.MetodosEntrega.FindAsync(id);
            if (metodo == null)
            {
                return NotFound();
            }

            var model = new MetodoEntregaViewModel
            {
                IdMetodoEntrega = metodo.IdMetodoEntrega,
                Nombre = metodo.Nombre,
                Costo = metodo.Costo
            };

            return View(model);
        }

        // POST: MetodosEntrega/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, MetodoEntregaViewModel model)
        {
            if (id != model.IdMetodoEntrega)
            {
                return NotFound();
            }

            if (await _context.MetodosEntrega.AnyAsync(m => m.Nombre == model.Nombre && m.IdMetodoEntrega != id))
            {
                ModelState.AddModelError(nameof(MetodoEntregaViewModel.Nombre), "Ya existe un método de entrega con ese nombre.");
            }

            if (ModelState.IsValid)
            {
                var existing = await _context.MetodosEntrega.FindAsync(id);
                if (existing == null)
                {
                    return NotFound();
                }

                var antes = new { existing.Nombre, existing.Costo };

                existing.Nombre = model.Nombre;
                existing.Costo = model.Costo;

                try
                {
                    await _context.SaveChangesAsync();

                    await _auditoria.RegistrarAsync("Editar", "MetodoEntrega", id.ToString(),
                        antes,
                        new { existing.Nombre, existing.Costo });

                    TempData["Success"] = "Método de entrega actualizado correctamente.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!MetodoEntregaExists(model.IdMetodoEntrega))
                    {
                        return NotFound();
                    }
                    throw;
                }

                return RedirectToAction(nameof(Index));
            }

            return View(model);
        }

        // GET: MetodosEntrega/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var metodo = await _context.MetodosEntrega
                .Include(m => m.Pedidos)
                .FirstOrDefaultAsync(m => m.IdMetodoEntrega == id);
            if (metodo == null)
            {
                return NotFound();
            }

            return View(metodo);
        }

        // POST: MetodosEntrega/Delete/5 (hard delete bloqueado si tiene pedidos)
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var metodo = await _context.MetodosEntrega
                .Include(m => m.Pedidos)
                .FirstOrDefaultAsync(m => m.IdMetodoEntrega == id);

            if (metodo == null)
            {
                return NotFound();
            }

            if (metodo.Pedidos.Any())
            {
                TempData["Error"] = "No se puede eliminar el método de entrega porque tiene pedidos asociados.";
                return RedirectToAction(nameof(Index));
            }

            var antes = new { metodo.Nombre, metodo.Costo };

            _context.MetodosEntrega.Remove(metodo);
            await _context.SaveChangesAsync();

            await _auditoria.RegistrarAsync("Eliminar", "MetodoEntrega", id.ToString(), antes, null);

            TempData["Success"] = "Método de entrega eliminado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        private bool MetodoEntregaExists(int id)
        {
            return _context.MetodosEntrega.Any(m => m.IdMetodoEntrega == id);
        }
    }
}
