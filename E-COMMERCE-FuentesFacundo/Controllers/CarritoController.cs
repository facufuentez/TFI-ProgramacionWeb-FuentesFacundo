using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models.Entities;
using E_COMMERCE_FuentesFacundo.Models.Identity;
using E_COMMERCE_FuentesFacundo.Models.ViewModels;
using E_COMMERCE_FuentesFacundo.Services;
using System.Text.Json;

namespace E_COMMERCE_FuentesFacundo.Controllers
{
    public class CarritoController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly CartService _cart;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AuditoriaService _auditoria;
        private readonly MercadoPagoService _mercadoPago;

        public CarritoController(
            ApplicationDbContext context,
            CartService cart,
            UserManager<ApplicationUser> userManager,
            AuditoriaService auditoria,
            MercadoPagoService mercadoPago)
        {
            _context = context;
            _cart = cart;
            _userManager = userManager;
            _auditoria = auditoria;
            _mercadoPago = mercadoPago;
        }

        // GET: Carrito
        [AllowAnonymous]
        public async Task<IActionResult> Index()
        {
            var model = await BuildCartAsync();
            return View(model);
        }

        // POST: Carrito/Add
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(int idProducto, int cantidad = 1, string? returnUrl = null)
        {
            if (cantidad < 1)
            {
                cantidad = 1;
            }

            var producto = await _context.Productos
                .FirstOrDefaultAsync(p => p.IdProducto == idProducto);

            if (producto == null || !producto.Activo)
            {
                TempData["Error"] = "El producto no está disponible.";
                return RedirectBack(returnUrl);
            }

            var yaEnCarrito = _cart.GetLines()
                .FirstOrDefault(l => l.IdProducto == idProducto)?.Cantidad ?? 0;

            if (yaEnCarrito + cantidad > producto.Stock)
            {
                TempData["Error"] = $"No hay stock suficiente para '{producto.Nombre}'. Disponible: {producto.Stock}.";
                return RedirectBack(returnUrl);
            }

            _cart.Add(idProducto, cantidad);
            TempData["Success"] = $"Agregaste '{producto.Nombre}' al carrito.";
            return RedirectBack(returnUrl);
        }

        // POST: Carrito/Update
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int idProducto, int cantidad)
        {
            var producto = await _context.Productos
                .FirstOrDefaultAsync(p => p.IdProducto == idProducto);

            if (producto == null)
            {
                _cart.Remove(idProducto);
                TempData["Error"] = "El producto ya no existe y fue quitado del carrito.";
                return RedirectToAction(nameof(Index));
            }

            if (cantidad > producto.Stock)
            {
                TempData["Error"] = $"No hay stock suficiente para '{producto.Nombre}'. Disponible: {producto.Stock}.";
                return RedirectToAction(nameof(Index));
            }

            _cart.UpdateQty(idProducto, cantidad);
            TempData["Success"] = "Carrito actualizado.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Carrito/Remove
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public IActionResult Remove(int idProducto)
        {
            _cart.Remove(idProducto);
            TempData["Success"] = "Producto quitado del carrito.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Carrito/Clear
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public IActionResult Clear()
        {
            _cart.Clear();
            TempData["Success"] = "Carrito vaciado.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Carrito/Checkout
        [Authorize]
        public async Task<IActionResult> Checkout()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            if (!_cart.GetLines().Any())
            {
                TempData["Error"] = "Tu carrito está vacío.";
                return RedirectToAction(nameof(Index));
            }

            var model = await BuildCheckoutAsync(user.Id);
            return View(model);
        }

        // POST: Carrito/Checkout
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(CheckoutViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var lines = _cart.GetLines();
            if (!lines.Any())
            {
                TempData["Error"] = "Tu carrito está vacío.";
                return RedirectToAction(nameof(Index));
            }

            var ids = lines.Select(l => l.IdProducto).ToList();
            var productos = await _context.Productos
                .Where(p => ids.Contains(p.IdProducto))
                .ToListAsync();

            foreach (var line in lines)
            {
                var producto = productos.FirstOrDefault(p => p.IdProducto == line.IdProducto);
                if (producto == null)
                {
                    ModelState.AddModelError(string.Empty, "Uno de los productos del carrito ya no está disponible.");
                }
                else if (!producto.Activo)
                {
                    ModelState.AddModelError(string.Empty, $"El producto '{producto.Nombre}' ya no está disponible.");
                }
                else if (line.Cantidad > producto.Stock)
                {
                    ModelState.AddModelError(string.Empty, $"Stock insuficiente para '{producto.Nombre}'. Disponible: {producto.Stock}.");
                }
            }

            var direccion = await _context.Direcciones
                .FirstOrDefaultAsync(d => d.IdDireccion == model.IdDireccion
                                          && d.IdUsuario == user.Id
                                          && d.Activa);
            if (direccion == null)
            {
                ModelState.AddModelError(nameof(CheckoutViewModel.IdDireccion), "La dirección seleccionada no es válida.");
            }

            var metodo = await _context.MetodosEntrega
                .FirstOrDefaultAsync(m => m.IdMetodoEntrega == model.IdMetodoEntrega);
            if (metodo == null)
            {
                ModelState.AddModelError(nameof(CheckoutViewModel.IdMetodoEntrega), "El método de entrega seleccionado no es válido.");
            }

            if (!CheckoutViewModel.MediosPagoValidos.Contains(model.MetodoPago))
            {
                ModelState.AddModelError(nameof(CheckoutViewModel.MetodoPago), "Seleccioná un medio de pago válido.");
            }

            if (!ModelState.IsValid)
            {
                var invalidModel = await BuildCheckoutAsync(user.Id, model);
                return View(invalidModel);
            }

            if (string.Equals(model.MetodoPago, "Mercado Pago", StringComparison.OrdinalIgnoreCase) &&
                !_mercadoPago.EstaConfigurado)
            {
                TempData["Error"] = "Mercado Pago no está configurado. Agrega MP_ACCESS_TOKEN en el archivo .env.";
                return RedirectToAction(nameof(Checkout));
            }

            if (string.Equals(model.MetodoPago, "Mercado Pago", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(user.Email))
            {
                ModelState.AddModelError(string.Empty, "Tu cuenta no tiene un email válido para Mercado Pago.");
                var emailInvalidModel = await BuildCheckoutAsync(user.Id, model);
                return View(emailInvalidModel);
            }

            var estadoPendiente = await _context.EstadosPedido
                .FirstOrDefaultAsync(e => e.Nombre == "Pendiente");
            if (estadoPendiente == null)
            {
                TempData["Error"] = "No se pudo registrar el pedido: falta el estado 'Pendiente'.";
                return RedirectToAction(nameof(Index));
            }

            var subtotal = productos.Sum(p =>
            {
                var line = lines.First(l => l.IdProducto == p.IdProducto);
                return p.Precio * line.Cantidad;
            });
            var costoEnvio = metodo!.Costo;
            var total = subtotal + costoEnvio;

            var esMercadoPago = string.Equals(model.MetodoPago, "Mercado Pago", StringComparison.OrdinalIgnoreCase);

            if (esMercadoPago)
            {
                var checkoutItems = lines.Select(line =>
                {
                    var producto = productos.First(p => p.IdProducto == line.IdProducto);
                    return new MercadoPagoCheckoutItem(producto.IdProducto, producto.Nombre, producto.Precio, line.Cantidad);
                }).ToList();

                var checkout = new MercadoPagoCheckout
                {
                    IdUsuario = user.Id,
                    IdDireccionEnvio = direccion!.IdDireccion,
                    IdMetodoEntrega = metodo.IdMetodoEntrega,
                    Total = total,
                    CostoEnvio = costoEnvio,
                    ItemsJson = JsonSerializer.Serialize(checkoutItems),
                    Estado = "Pendiente",
                    Fecha = DateTime.Now
                };

                var successUrl = Url.Action("Success", "MercadoPago", null, Request.Scheme)!;
                var failureUrl = Url.Action("Failure", "MercadoPago", null, Request.Scheme)!;
                var pendingUrl = Url.Action("Pending", "MercadoPago", null, Request.Scheme)!;

                await using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    _context.MercadoPagoCheckouts.Add(checkout);
                    await _context.SaveChangesAsync();

                    MercadoPagoOrderResult mpOrder;
                    try
                    {
                        mpOrder = await _mercadoPago.CrearOrderAsync(
                            checkout,
                            user.Email!,
                            successUrl,
                            failureUrl,
                            pendingUrl,
                            CancellationToken.None);
                    }
                    catch (MercadoPagoException ex)
                    {
                        await transaction.RollbackAsync();
                        TempData["Error"] = ex.UserMessage;
                        return RedirectToAction(nameof(Checkout));
                    }

                    checkout.IdMercadoPago = mpOrder.OrderId;
                    checkout.CheckoutUrl = mpOrder.CheckoutUrl;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    await _auditoria.RegistrarAsync("Crear", "CheckoutMercadoPago", checkout.Id.ToString(), null, new
                    {
                        checkout.IdUsuario,
                        Total = total,
                        Items = lines.Count,
                        MercadoPagoOrderId = mpOrder.OrderId
                    });

                    TempData["MercadoPagoCheckoutUrl"] = mpOrder.CheckoutUrl;
                    TempData["MercadoPagoCheckoutId"] = checkout.Id;
                    return RedirectToAction("MercadoPagoCheckout", "Carrito");
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    TempData["Error"] = "No se pudo iniciar el pago con Mercado Pago. Intenta nuevamente.";
                    return RedirectToAction(nameof(Checkout));
                }
            }

            await using var pedidoTransaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var pedido = new Pedido
                {
                    IdUsuario = user.Id,
                    IdEstadoPedido = estadoPendiente.IdEstadoPedido,
                    IdMetodoEntrega = metodo.IdMetodoEntrega,
                    IdDireccionEnvio = direccion!.IdDireccion,
                    Fecha = DateTime.Now,
                    CostoEnvio = costoEnvio
                };

                foreach (var line in lines)
                {
                    var producto = productos.First(p => p.IdProducto == line.IdProducto);
                    pedido.DetallesPedido.Add(new DetallePedido
                    {
                        IdProducto = producto.IdProducto,
                        Cantidad = line.Cantidad,
                        PrecioUnitario = producto.Precio
                    });
                    producto.Stock -= line.Cantidad;
                }

                pedido.Pagos.Add(new Pago
                {
                    MetodoPago = model.MetodoPago,
                    EstadoPago = "Pendiente",
                    Monto = total,
                    FechaPago = null
                });

                _context.Pedidos.Add(pedido);
                await _context.SaveChangesAsync();

                await pedidoTransaction.CommitAsync();

                await _auditoria.RegistrarAsync("Crear", "Pedido", pedido.IdPedido.ToString(), null, new
                {
                    pedido.IdUsuario,
                    Total = total,
                    MetodoPago = model.MetodoPago,
                    Items = lines.Count,
                    Estado = estadoPendiente.Nombre
                });

                _cart.Clear();
                TempData["Success"] = "Pedido registrado correctamente. Aguardamos la confirmacion del pago.";
                return RedirectToAction("Details", "Pedidos", new { id = pedido.IdPedido });
            }
            catch (Exception)
            {
                await pedidoTransaction.RollbackAsync();
                TempData["Error"] = "No se pudo registrar el pedido. Intenta nuevamente.";
                return RedirectToAction(nameof(Checkout));
            }
        }

        [Authorize]
        public async Task<IActionResult> MercadoPagoCheckout(Guid? id)
        {
            var checkoutId = id ?? (TempData["MercadoPagoCheckoutId"] as Guid?);
            if (!checkoutId.HasValue)
            {
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            var checkout = await _context.MercadoPagoCheckouts
                .FirstOrDefaultAsync(c => c.Id == checkoutId.Value && c.IdUsuario == user.Id);

            if (checkout is null || string.IsNullOrWhiteSpace(checkout.CheckoutUrl))
            {
                return RedirectToAction(nameof(Index));
            }

            return View(new MercadoPagoCheckoutViewModel
            {
                CheckoutUrl = checkout.CheckoutUrl,
                CheckoutId = checkout.Id
            });
        }

        private async Task<CartViewModel> BuildCartAsync()
        {
            var lines = _cart.GetLines();
            var ids = lines.Select(l => l.IdProducto).ToList();
            var productos = await _context.Productos
                .Where(p => ids.Contains(p.IdProducto))
                .ToListAsync();

            var items = new List<CartItemViewModel>();
            foreach (var line in lines)
            {
                var producto = productos.FirstOrDefault(p => p.IdProducto == line.IdProducto);
                if (producto == null)
                {
                    _cart.Remove(line.IdProducto);
                    continue;
                }

                items.Add(MapItem(producto, line.Cantidad));
            }

            return new CartViewModel { Items = items };
        }

        private async Task<CheckoutViewModel> BuildCheckoutAsync(string userId, CheckoutViewModel? source = null)
        {
            var lines = _cart.GetLines();
            var ids = lines.Select(l => l.IdProducto).ToList();
            var productos = await _context.Productos
                .Where(p => ids.Contains(p.IdProducto))
                .ToListAsync();

            var items = new List<CartItemViewModel>();
            foreach (var line in lines)
            {
                var producto = productos.FirstOrDefault(p => p.IdProducto == line.IdProducto);
                if (producto != null)
                {
                    items.Add(MapItem(producto, line.Cantidad));
                }
            }

            var direcciones = await _context.Direcciones
                .Where(d => d.IdUsuario == userId && d.Activa)
                .Select(d => new
                {
                    d.IdDireccion,
                    d.Calle,
                    d.Numero,
                    d.Ciudad,
                    Provincia = d.Provincia!.Nombre
                })
                .ToListAsync();

            var metodos = await _context.MetodosEntrega
                .OrderBy(m => m.Nombre)
                .ToListAsync();

            var model = new CheckoutViewModel
            {
                Items = items,
                Subtotal = items.Sum(i => i.Subtotal),
                Direcciones = direcciones.Select(d => new SelectListItem
                {
                    Value = d.IdDireccion.ToString(),
                    Text = $"{d.Calle} {d.Numero}, {d.Ciudad} ({d.Provincia})"
                }).ToList(),
                MetodosEntrega = metodos.Select(m => new MetodoEntregaOpcion
                {
                    Id = m.IdMetodoEntrega,
                    Nombre = m.Nombre,
                    Costo = m.Costo
                }).ToList(),
                MetodosPago = CheckoutViewModel.MediosPagoValidos
                    .Select(m => new SelectListItem { Value = m, Text = m })
                    .ToList()
            };

            if (source != null)
            {
                model.IdDireccion = source.IdDireccion;
                model.IdMetodoEntrega = source.IdMetodoEntrega;
                model.MetodoPago = source.MetodoPago;
            }

            if (model.IdDireccion == 0 && model.Direcciones.Any())
            {
                model.IdDireccion = int.Parse(model.Direcciones[0].Value!);
            }

            if (model.IdMetodoEntrega == 0 && model.MetodosEntrega.Any())
            {
                model.IdMetodoEntrega = model.MetodosEntrega[0].Id;
            }

            if (string.IsNullOrEmpty(model.MetodoPago) && model.MetodosPago.Any())
            {
                model.MetodoPago = model.MetodosPago[0].Value ?? string.Empty;
            }

            var metodoSeleccionado = model.MetodosEntrega.FirstOrDefault(m => m.Id == model.IdMetodoEntrega);
            model.CostoEnvio = metodoSeleccionado?.Costo ?? 0;
            model.Total = model.Subtotal + model.CostoEnvio;

            return model;
        }

        private static CartItemViewModel MapItem(Producto producto, int cantidad)
        {
            return new CartItemViewModel
            {
                IdProducto = producto.IdProducto,
                Nombre = producto.Nombre,
                Precio = producto.Precio,
                Cantidad = cantidad,
                Stock = producto.Stock,
                ImagenUrl = producto.ImagenUrl,
                Activo = producto.Activo
            };
        }

        private IActionResult RedirectBack(string? returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
