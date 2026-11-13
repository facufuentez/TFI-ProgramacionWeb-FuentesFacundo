using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models.Entities;
using E_COMMERCE_FuentesFacundo.Services;

namespace E_COMMERCE_FuentesFacundo.Controllers
{
    public class ProductosController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly AuditoriaService _auditoria;
        private readonly ILogger<ProductosController> _logger;

        public ProductosController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment, AuditoriaService auditoria, ILogger<ProductosController> logger)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _auditoria = auditoria;
            _logger = logger;
        }

        // GET: Productos (administración)
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> Index()
        {
            var productos = await _context.Productos
                .Include(p => p.Categoria)
                .ToListAsync();
            return View(productos);
        }

        // GET: Productos/Details/5
        [AllowAnonymous]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var producto = await _context.Productos
                .Include(p => p.Categoria)
                .FirstOrDefaultAsync(m => m.IdProducto == id);
            if (producto == null)
            {
                return NotFound();
            }

            return View(producto);
        }

        // GET: Productos/Create
        [Authorize(Roles = "Administrador")]
        public IActionResult Create()
        {
            ViewData["IdCategoria"] = new SelectList(_context.Categorias, "IdCategoria", "Nombre");
            return View();
        }

        // POST: Productos/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> Create([Bind("IdProducto,IdCategoria,Nombre,Descripcion,Precio,Stock,Activo")] Producto producto, IFormFile? imagenArchivo)
        {
            if (ModelState.IsValid)
            {
                if (imagenArchivo != null && imagenArchivo.Length > 0)
                {
                    var (imagenUrl, _, error) = await GuardarImagenAsync(imagenArchivo);
                    if (error != null)
                    {
                        ModelState.AddModelError("ImagenUrl", error);
                        ViewData["IdCategoria"] = new SelectList(_context.Categorias, "IdCategoria", "Nombre", producto.IdCategoria);
                        return View(producto);
                    }

                    producto.ImagenUrl = imagenUrl;
                }

                _context.Add(producto);
                await _context.SaveChangesAsync();

                await _auditoria.RegistrarAsync("Crear", "Producto", producto.IdProducto.ToString(), null, new
                {
                    producto.Nombre,
                    producto.IdCategoria,
                    producto.Precio,
                    producto.Stock,
                    producto.Activo,
                    producto.ImagenUrl
                });

                TempData["Success"] = "Producto creado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            ViewData["IdCategoria"] = new SelectList(_context.Categorias, "IdCategoria", "Nombre", producto.IdCategoria);
            return View(producto);
        }

        // GET: Productos/Edit/5
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var producto = await _context.Productos.FindAsync(id);
            if (producto == null)
            {
                return NotFound();
            }
            ViewData["IdCategoria"] = new SelectList(_context.Categorias, "IdCategoria", "Nombre", producto.IdCategoria);
            return View(producto);
        }

        // POST: Productos/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> Edit(int id, [Bind("IdProducto,IdCategoria,Nombre,Descripcion,Precio,Stock,ImagenUrl,Activo")] Producto producto, IFormFile? imagenArchivo)
        {
            if (id != producto.IdProducto)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                var antes = await _context.Productos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.IdProducto == id);

                string? oldImageUrl = producto.ImagenUrl;
                string? newImagePath = null;
                bool saved = false;

                try
                {
                    if (imagenArchivo != null && imagenArchivo.Length > 0)
                    {
                        var (imagenUrl, filePath, error) = await GuardarImagenAsync(imagenArchivo);
                        if (error != null)
                        {
                            ModelState.AddModelError("ImagenUrl", error);
                            ViewData["IdCategoria"] = new SelectList(_context.Categorias, "IdCategoria", "Nombre", producto.IdCategoria);
                            return View(producto);
                        }

                        newImagePath = filePath;
                        producto.ImagenUrl = imagenUrl;
                    }

                    _context.Update(producto);
                    await _context.SaveChangesAsync();
                    saved = true;

                    if (!string.IsNullOrEmpty(oldImageUrl) && oldImageUrl != producto.ImagenUrl)
                    {
                        EliminarArchivoSiExiste(ObtenerRutaImagenLocal(oldImageUrl));
                    }

                    await _auditoria.RegistrarAsync("Editar", "Producto", id.ToString(),
                        antes == null ? null : new
                        {
                            antes.Nombre,
                            antes.IdCategoria,
                            antes.Precio,
                            antes.Stock,
                            antes.Activo,
                            antes.ImagenUrl
                        },
                        new
                        {
                            producto.Nombre,
                            producto.IdCategoria,
                            producto.Precio,
                            producto.Stock,
                            producto.Activo,
                            producto.ImagenUrl
                        });
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ProductoExists(producto.IdProducto))
                    {
                        EliminarArchivoSiExiste(newImagePath);
                        return NotFound();
                    }

                    EliminarArchivoSiExiste(newImagePath);
                    throw;
                }
                catch
                {
                    if (!saved)
                    {
                        EliminarArchivoSiExiste(newImagePath);
                    }

                    throw;
                }
                TempData["Success"] = "Producto actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            ViewData["IdCategoria"] = new SelectList(_context.Categorias, "IdCategoria", "Nombre", producto.IdCategoria);
            return View(producto);
        }

        // GET: Productos/Delete/5
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var producto = await _context.Productos
                .Include(p => p.Categoria)
                .FirstOrDefaultAsync(m => m.IdProducto == id);
            if (producto == null)
            {
                return NotFound();
            }

            return View(producto);
        }

        // POST: Productos/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var producto = await _context.Productos.FindAsync(id);
            if (producto != null)
            {
                var tienePedidos = await _context.DetallesPedido.AnyAsync(d => d.IdProducto == id);
                if (tienePedidos)
                {
                    TempData["Error"] = "No se puede eliminar el producto porque tiene pedidos asociados. Podés marcarlo como inactivo.";
                    return RedirectToAction(nameof(Index));
                }

                // Eliminar imagen física si existe
                EliminarArchivoSiExiste(ObtenerRutaImagenLocal(producto.ImagenUrl));

                _context.Productos.Remove(producto);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Producto eliminado correctamente.";

                await _auditoria.RegistrarAsync("Eliminar", "Producto", id.ToString(), new
                {
                    producto.Nombre,
                    producto.IdCategoria,
                    producto.Precio,
                    producto.Stock,
                    producto.Activo,
                    producto.ImagenUrl
                }, null);
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<(string? Url, string? FilePath, string? Error)> GuardarImagenAsync(IFormFile imagenArchivo)
        {
            try
            {
                string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "productos");
                Directory.CreateDirectory(uploadsFolder);

                string extension = Path.GetExtension(imagenArchivo.FileName).ToLowerInvariant();
                string[] allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                if (!allowedExtensions.Contains(extension))
                {
                    return (null, null, "Solo se permiten archivos de imagen (.jpg, .jpeg, .png, .gif, .webp)");
                }

                string uniqueFileName = Guid.NewGuid().ToString() + extension;
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await imagenArchivo.CopyToAsync(fileStream);
                }

                _logger.LogInformation("Imagen guardada en {FilePath}", filePath);
                return ("/images/productos/" + uniqueFileName, filePath, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "No se pudo guardar la imagen {FileName}", imagenArchivo.FileName);
                return (null, null, "No se pudo guardar la imagen. Verificá los permisos de escritura del servidor.");
            }
        }

        private string? ObtenerRutaImagenLocal(string? imagenUrl)
        {
            if (string.IsNullOrWhiteSpace(imagenUrl) ||
                !imagenUrl.StartsWith("/images/productos/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string uploadsFolder = Path.GetFullPath(
                Path.Combine(_webHostEnvironment.WebRootPath, "images", "productos"));
            string imagePath = Path.GetFullPath(
                Path.Combine(_webHostEnvironment.WebRootPath, imagenUrl.TrimStart('/')));

            if (!imagePath.StartsWith(uploadsFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return imagePath;
        }

        private void EliminarArchivoSiExiste(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                return;
            }

            try
            {
                System.IO.File.Delete(filePath);
                _logger.LogInformation("Archivo eliminado: {FilePath}", filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "No se pudo eliminar el archivo {FilePath}", filePath);
            }
        }

        private bool ProductoExists(int id)
        {
            return _context.Productos.Any(e => e.IdProducto == id);
        }
    }
}
