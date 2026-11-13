using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models.Entities;
using E_COMMERCE_FuentesFacundo.Services;

namespace E_COMMERCE_FuentesFacundo.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class CategoriasController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditoriaService _auditoria;

        public CategoriasController(ApplicationDbContext context, AuditoriaService auditoria)
        {
            _context = context;
            _auditoria = auditoria;
        }

        // GET: Categorias
        public async Task<IActionResult> Index()
        {
            var categorias = await _context.Categorias
                .Include(c => c.Productos)
                .ToListAsync();
            return View(categorias);
        }

        // GET: Categorias/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var categoria = await _context.Categorias
                .Include(c => c.Productos)
                .FirstOrDefaultAsync(m => m.IdCategoria == id);
            if (categoria == null)
            {
                return NotFound();
            }

            return View(categoria);
        }

        // GET: Categorias/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Categorias/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("IdCategoria,Nombre,Descripcion")] Categoria categoria)
        {
            if (await _context.Categorias.AnyAsync(c => c.Nombre == categoria.Nombre))
            {
                ModelState.AddModelError(nameof(Categoria.Nombre), "Ya existe una categoría con ese nombre.");
            }

            if (ModelState.IsValid)
            {
                _context.Add(categoria);
                await _context.SaveChangesAsync();

                await _auditoria.RegistrarAsync("Crear", "Categoria", categoria.IdCategoria.ToString(), null, new
                {
                    categoria.Nombre,
                    categoria.Descripcion
                });

                TempData["Success"] = "Categoría creada correctamente.";
                return RedirectToAction(nameof(Index));
            }
            return View(categoria);
        }

        // GET: Categorias/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var categoria = await _context.Categorias.FindAsync(id);
            if (categoria == null)
            {
                return NotFound();
            }
            return View(categoria);
        }

        // POST: Categorias/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("IdCategoria,Nombre,Descripcion")] Categoria categoria)
        {
            if (id != categoria.IdCategoria)
            {
                return NotFound();
            }

            if (await _context.Categorias.AnyAsync(c => c.Nombre == categoria.Nombre && c.IdCategoria != id))
            {
                ModelState.AddModelError(nameof(Categoria.Nombre), "Ya existe una categoría con ese nombre.");
            }

            if (ModelState.IsValid)
            {
                var antes = await _context.Categorias
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.IdCategoria == id);

                try
                {
                    _context.Update(categoria);
                    await _context.SaveChangesAsync();

                    await _auditoria.RegistrarAsync("Editar", "Categoria", id.ToString(),
                        antes == null ? null : new { antes.Nombre, antes.Descripcion },
                        new { categoria.Nombre, categoria.Descripcion });

                    TempData["Success"] = "Categoría actualizada correctamente.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!CategoriaExists(categoria.IdCategoria))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(categoria);
        }

        // GET: Categorias/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var categoria = await _context.Categorias
                .Include(c => c.Productos)
                .FirstOrDefaultAsync(m => m.IdCategoria == id);
            if (categoria == null)
            {
                return NotFound();
            }

            return View(categoria);
        }

        // POST: Categorias/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var categoria = await _context.Categorias
                .Include(c => c.Productos)
                .FirstOrDefaultAsync(c => c.IdCategoria == id);

            if (categoria == null)
            {
                return NotFound();
            }

            if (categoria.Productos.Any())
            {
                TempData["Error"] = "No se puede eliminar la categoría porque tiene productos asociados.";
                return RedirectToAction(nameof(Index));
            }

            _context.Categorias.Remove(categoria);
            await _context.SaveChangesAsync();

            await _auditoria.RegistrarAsync("Eliminar", "Categoria", id.ToString(),
                new { categoria.Nombre, categoria.Descripcion }, null);

            TempData["Success"] = "Categoría eliminada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        private bool CategoriaExists(int id)
        {
            return _context.Categorias.Any(e => e.IdCategoria == id);
        }
    }
}
