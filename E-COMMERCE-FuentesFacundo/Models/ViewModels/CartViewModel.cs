namespace E_COMMERCE_FuentesFacundo.Models.ViewModels
{
    public class CartItemViewModel
    {
        public int IdProducto { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public int Cantidad { get; set; }
        public int Stock { get; set; }
        public string? ImagenUrl { get; set; }
        public bool Activo { get; set; }

        public decimal Subtotal => Precio * Cantidad;

        public bool Disponible => Activo && Stock > 0;
        public bool StockInsuficiente => Cantidad > Stock;
    }

    public class CartViewModel
    {
        public List<CartItemViewModel> Items { get; set; } = new();
        public decimal Subtotal => Items.Sum(i => i.Subtotal);
        public int CantidadTotal => Items.Sum(i => i.Cantidad);
        public bool TieneProblemas => Items.Any(i => !i.Disponible || i.StockInsuficiente);
    }

    public class MetodoEntregaOpcion
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public decimal Costo { get; set; }
    }

    public class MercadoPagoCheckoutViewModel
    {
        public string CheckoutUrl { get; set; } = string.Empty;
        public Guid CheckoutId { get; set; }
    }

    public class CheckoutViewModel
    {
        public List<CartItemViewModel> Items { get; set; } = new();
        public decimal Subtotal { get; set; }
        public decimal CostoEnvio { get; set; }
        public decimal Total { get; set; }

        public int IdDireccion { get; set; }
        public int IdMetodoEntrega { get; set; }
        public string MetodoPago { get; set; } = string.Empty;

        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Direcciones { get; set; } = new();
        public List<MetodoEntregaOpcion> MetodosEntrega { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> MetodosPago { get; set; } = new();

        public static readonly string[] MediosPagoValidos = { "Efectivo", "Mercado Pago" };
    }
}
