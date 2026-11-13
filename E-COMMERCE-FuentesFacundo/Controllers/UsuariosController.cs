using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models;
using E_COMMERCE_FuentesFacundo.Models.Identity;
using E_COMMERCE_FuentesFacundo.Models.ViewModels;
using E_COMMERCE_FuentesFacundo.Services;

namespace E_COMMERCE_FuentesFacundo.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class UsuariosController : Controller
    {
        private const string RolAdministrador = "Administrador";

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly AuditoriaService _auditoria;

        public UsuariosController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext context,
            AuditoriaService auditoria)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
            _auditoria = auditoria;
        }

        // GET: Usuarios
        public async Task<IActionResult> Index(int page = 1)
        {
            var usuariosPagina = await PaginatedList<ApplicationUser>.CreateAsync(
                _userManager.Users
                    .OrderBy(u => u.Apellido)
                    .ThenBy(u => u.Nombre),
                page,
                PaginatedList<ApplicationUser>.DefaultPageSize);

            var ids = usuariosPagina.Select(u => u.Id).ToList();
            var pedidosPorUsuario = await _context.Pedidos
                .Where(p => ids.Contains(p.IdUsuario))
                .GroupBy(p => p.IdUsuario)
                .Select(g => new { g.Key, Cantidad = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Cantidad);

            var items = new List<UsuarioListaViewModel>();
            foreach (var usuario in usuariosPagina)
            {
                var roles = await _userManager.GetRolesAsync(usuario);
                items.Add(new UsuarioListaViewModel
                {
                    Id = usuario.Id,
                    Nombre = usuario.Nombre,
                    Apellido = usuario.Apellido,
                    Email = usuario.Email ?? string.Empty,
                    Roles = roles,
                    CantidadPedidos = pedidosPorUsuario.TryGetValue(usuario.Id, out var cantidad) ? cantidad : 0
                });
            }

            var model = new PaginatedList<UsuarioListaViewModel>(
                items,
                usuariosPagina.TotalCount,
                usuariosPagina.PageIndex,
                usuariosPagina.PageSize);

            return View(model);
        }

        // GET: Usuarios/Edit/5
        public async Task<IActionResult> Edit(string? id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var usuario = await _userManager.FindByIdAsync(id);
            if (usuario == null)
            {
                return NotFound();
            }

            var rolesSeleccionados = await _userManager.GetRolesAsync(usuario);

            var model = new UsuarioRolesViewModel
            {
                IdUsuario = usuario.Id,
                NombreCompleto = $"{usuario.Nombre} {usuario.Apellido}".Trim(),
                Email = usuario.Email ?? string.Empty,
                RolesDisponibles = await ObtenerRolesAsync(),
                RolesSeleccionados = rolesSeleccionados.ToList()
            };

            return View(model);
        }

        // POST: Usuarios/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, UsuarioRolesViewModel model)
        {
            if (id != model.IdUsuario)
            {
                return NotFound();
            }

            var usuario = await _userManager.FindByIdAsync(id);
            if (usuario == null)
            {
                return NotFound();
            }

            var rolesDisponibles = await ObtenerRolesAsync();
            model.RolesDisponibles = rolesDisponibles;
            model.NombreCompleto = $"{usuario.Nombre} {usuario.Apellido}".Trim();
            model.Email = usuario.Email ?? string.Empty;
            model.RolesSeleccionados = (model.RolesSeleccionados ?? new List<string>())
                .Where(r => rolesDisponibles.Contains(r))
                .Distinct()
                .ToList();

            var rolesActuales = await _userManager.GetRolesAsync(usuario);

            if (!model.RolesSeleccionados.Any())
            {
                ModelState.AddModelError(nameof(UsuarioRolesViewModel.RolesSeleccionados), "Seleccioná al menos un rol.");
            }

            var esUnoMismo = _userManager.GetUserId(User) == usuario.Id;
            if (esUnoMismo
                && rolesActuales.Contains(RolAdministrador)
                && !model.RolesSeleccionados.Contains(RolAdministrador))
            {
                ModelState.AddModelError(nameof(UsuarioRolesViewModel.RolesSeleccionados), "No podés quitarte a vos mismo el rol Administrador.");
            }

            if (rolesActuales.Contains(RolAdministrador)
                && !model.RolesSeleccionados.Contains(RolAdministrador))
            {
                var administradores = await _userManager.GetUsersInRoleAsync(RolAdministrador);
                if (administradores.Count <= 1)
                {
                    ModelState.AddModelError(nameof(UsuarioRolesViewModel.RolesSeleccionados), "No se puede dejar el sistema sin ningún administrador.");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var agregar = model.RolesSeleccionados.Except(rolesActuales).ToList();
            var quitar = rolesActuales.Except(model.RolesSeleccionados).ToList();

            var huboCambios = false;

            if (agregar.Any())
            {
                var resultado = await _userManager.AddToRolesAsync(usuario, agregar);
                if (!resultado.Succeeded)
                {
                    foreach (var error in resultado.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                }
                else
                {
                    huboCambios = true;
                }
            }

            if (quitar.Any() && ModelState.IsValid)
            {
                var resultado = await _userManager.RemoveFromRolesAsync(usuario, quitar);
                if (!resultado.Succeeded)
                {
                    foreach (var error in resultado.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                }
                else
                {
                    huboCambios = true;
                }
            }

            if (!ModelState.IsValid)
            {
                var rolesActualesActualizados = await _userManager.GetRolesAsync(usuario);
                model.RolesSeleccionados = rolesActualesActualizados.ToList();
                return View(model);
            }

            if (huboCambios)
            {
                var rolesFinales = await _userManager.GetRolesAsync(usuario);
                await _auditoria.RegistrarAsync("EditarRoles", "Usuario", usuario.Id,
                    new { Roles = rolesActuales },
                    new { Roles = rolesFinales });
            }

            TempData["Success"] = "Roles actualizados correctamente.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Usuarios/ResetPassword/5
        public async Task<IActionResult> ResetPassword(string? id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var usuario = await _userManager.FindByIdAsync(id);
            if (usuario == null)
            {
                return NotFound();
            }

            return View(new ResetPasswordViewModel
            {
                IdUsuario = usuario.Id,
                Email = usuario.Email ?? string.Empty
            });
        }

        // POST: Usuarios/ResetPassword/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string id, ResetPasswordViewModel model)
        {
            if (id != model.IdUsuario)
            {
                return NotFound();
            }

            var usuario = await _userManager.FindByIdAsync(id);
            if (usuario == null)
            {
                return NotFound();
            }

            model.Email = usuario.Email ?? string.Empty;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(usuario);
            var resultado = await _userManager.ResetPasswordAsync(usuario, token, model.NuevaPassword);

            if (resultado.Succeeded)
            {
                await _auditoria.RegistrarAsync("ResetPassword", "Usuario", usuario.Id, null,
                    new { usuario.Email });

                TempData["Success"] = "Contraseña restablecida correctamente.";
                return RedirectToAction(nameof(Index));
            }

            foreach (var error in resultado.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        private async Task<List<string>> ObtenerRolesAsync()
        {
            return await _roleManager.Roles
                .Select(r => r.Name!)
                .OrderBy(r => r)
                .ToListAsync();
        }
    }
}
