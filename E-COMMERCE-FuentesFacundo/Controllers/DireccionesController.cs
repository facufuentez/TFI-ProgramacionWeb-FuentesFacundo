using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models.Entities;
using E_COMMERCE_FuentesFacundo.Models.Identity;
using E_COMMERCE_FuentesFacundo.Models.ViewModels;

namespace E_COMMERCE_FuentesFacundo.Controllers
{
    [Authorize]
    public class DireccionesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public DireccionesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Direcciones
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var direcciones = await _context.Direcciones
                .Include(d => d.Provincia)
                .Where(d => d.IdUsuario == user.Id)
                .OrderByDescending(d => d.Activa)
                .ThenBy(d => d.IdDireccion)
                .ToListAsync();

            return View(direcciones);
        }

        // GET: Direcciones/Create
        public async Task<IActionResult> Create()
        {
            await LoadProvinciasAsync();
            return View(new DireccionViewModel());
        }

        // POST: Direcciones/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DireccionViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            await ValidateProvinciaAsync(model.IdProvincia);

            if (ModelState.IsValid)
            {
                var direccion = new Direccion
                {
                    IdUsuario = user.Id,
                    Calle = model.Calle,
                    Numero = model.Numero,
                    Ciudad = model.Ciudad,
                    CodigoPostal = model.CodigoPostal,
                    IdProvincia = model.IdProvincia,
                    Activa = true
                };

                _context.Add(direccion);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Dirección creada correctamente.";
                return RedirectToAction(nameof(Index));
            }

            await LoadProvinciasAsync(model.IdProvincia);
            return View(model);
        }

        // GET: Direcciones/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var direccion = await _context.Direcciones
                .FirstOrDefaultAsync(d => d.IdDireccion == id && d.IdUsuario == user.Id);
            if (direccion == null)
            {
                return NotFound();
            }

            var model = new DireccionViewModel
            {
                IdDireccion = direccion.IdDireccion,
                Calle = direccion.Calle,
                Numero = direccion.Numero,
                Ciudad = direccion.Ciudad,
                CodigoPostal = direccion.CodigoPostal,
                IdProvincia = direccion.IdProvincia
            };

            await LoadProvinciasAsync(model.IdProvincia);
            return View(model);
        }

        // POST: Direcciones/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DireccionViewModel model)
        {
            if (id != model.IdDireccion)
            {
                return NotFound();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var existing = await _context.Direcciones
                .FirstOrDefaultAsync(d => d.IdDireccion == id && d.IdUsuario == user.Id);
            if (existing == null)
            {
                return NotFound();
            }

            await ValidateProvinciaAsync(model.IdProvincia);

            if (ModelState.IsValid)
            {
                existing.Calle = model.Calle;
                existing.Numero = model.Numero;
                existing.Ciudad = model.Ciudad;
                existing.CodigoPostal = model.CodigoPostal;
                existing.IdProvincia = model.IdProvincia;

                try
                {
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Dirección actualizada correctamente.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Direcciones.AnyAsync(d => d.IdDireccion == id))
                    {
                        return NotFound();
                    }
                    throw;
                }

                return RedirectToAction(nameof(Index));
            }

            await LoadProvinciasAsync(model.IdProvincia);
            return View(model);
        }

        // GET: Direcciones/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var direccion = await _context.Direcciones
                .Include(d => d.Provincia)
                .FirstOrDefaultAsync(d => d.IdDireccion == id && d.IdUsuario == user.Id);
            if (direccion == null)
            {
                return NotFound();
            }

            return View(direccion);
        }

        // POST: Direcciones/Delete/5 (soft delete)
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var direccion = await _context.Direcciones
                .FirstOrDefaultAsync(d => d.IdDireccion == id && d.IdUsuario == user.Id);
            if (direccion == null)
            {
                return NotFound();
            }

            direccion.Activa = false;
            await _context.SaveChangesAsync();
            TempData["Success"] = "Dirección desactivada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Direcciones/Reactivate/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reactivate(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var direccion = await _context.Direcciones
                .FirstOrDefaultAsync(d => d.IdDireccion == id && d.IdUsuario == user.Id);
            if (direccion == null)
            {
                return NotFound();
            }

            direccion.Activa = true;
            await _context.SaveChangesAsync();
            TempData["Success"] = "Dirección reactivada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        private async Task LoadProvinciasAsync(int? selectedId = null)
        {
            var provincias = await _context.Provincias
                .OrderBy(p => p.Nombre)
                .ToListAsync();
            ViewData["IdProvincia"] = new SelectList(provincias, "IdProvincia", "Nombre", selectedId);
        }

        private async Task ValidateProvinciaAsync(int idProvincia)
        {
            if (!await _context.Provincias.AnyAsync(p => p.IdProvincia == idProvincia))
            {
                ModelState.AddModelError(nameof(DireccionViewModel.IdProvincia), "La provincia seleccionada no es válida.");
            }
        }
    }
}
