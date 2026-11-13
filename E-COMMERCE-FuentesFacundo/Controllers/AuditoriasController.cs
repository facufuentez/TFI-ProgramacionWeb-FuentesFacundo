using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models;
using E_COMMERCE_FuentesFacundo.Models.Entities;

namespace E_COMMERCE_FuentesFacundo.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class AuditoriasController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AuditoriasController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Auditorias
        public async Task<IActionResult> Index(
            string? usuario,
            string? entidad,
            string? accion,
            DateTime? desde,
            DateTime? hasta,
            int page = 1)
        {
            var query = _context.Auditorias
                .Include(a => a.Usuario)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(usuario))
            {
                var like = $"%{usuario.Trim()}%";
                query = query.Where(a => a.Usuario != null && (
                    EF.Functions.Like(a.Usuario.Nombre, like)
                    || EF.Functions.Like(a.Usuario.Apellido, like)
                    || (a.Usuario.Email != null && EF.Functions.Like(a.Usuario.Email, like))));
            }

            if (!string.IsNullOrWhiteSpace(entidad))
            {
                query = query.Where(a => a.Entidad == entidad);
            }

            if (!string.IsNullOrWhiteSpace(accion))
            {
                query = query.Where(a => a.Accion == accion);
            }

            if (desde.HasValue)
            {
                query = query.Where(a => a.Fecha >= desde.Value.Date);
            }

            if (hasta.HasValue)
            {
                var fin = hasta.Value.Date.AddDays(1);
                query = query.Where(a => a.Fecha < fin);
            }

            var auditorias = await PaginatedList<Auditoria>.CreateAsync(
                query.OrderByDescending(a => a.Fecha).ThenByDescending(a => a.IdAuditoria),
                page,
                PaginatedList<Auditoria>.DefaultPageSize);

            ViewData["Usuario"] = usuario;
            ViewData["Entidad"] = entidad;
            ViewData["Accion"] = accion;
            ViewData["Desde"] = desde?.ToString("yyyy-MM-dd");
            ViewData["Hasta"] = hasta?.ToString("yyyy-MM-dd");

            ViewBag.Entidades = await _context.Auditorias
                .Select(a => a.Entidad)
                .Distinct()
                .OrderBy(e => e)
                .ToListAsync();

            ViewBag.Acciones = await _context.Auditorias
                .Select(a => a.Accion)
                .Distinct()
                .OrderBy(a => a)
                .ToListAsync();

            return View(auditorias);
        }

        // GET: Auditorias/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var auditoria = await _context.Auditorias
                .Include(a => a.Usuario)
                .FirstOrDefaultAsync(a => a.IdAuditoria == id);

            if (auditoria == null)
            {
                return NotFound();
            }

            ViewData["Antes"] = FormatearJson(auditoria.ValoresAntes);
            ViewData["Despues"] = FormatearJson(auditoria.ValoresDespues);

            return View(auditoria);
        }

        private static string? FormatearJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var documento = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(documento.RootElement, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }
            catch (JsonException)
            {
                return json;
            }
        }
    }
}
