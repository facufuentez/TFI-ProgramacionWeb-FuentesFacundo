using E_COMMERCE_FuentesFacundo.Models.Entities;

namespace E_COMMERCE_FuentesFacundo.Models.ViewModels
{
    public class ReportePedidosViewModel
    {
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }
        public string? Estado { get; set; }
        public List<Pedido> Pedidos { get; set; } = new();

        public decimal Total => Pedidos.Sum(p =>
            p.DetallesPedido.Sum(d => d.Cantidad * d.PrecioUnitario) + p.CostoEnvio);
    }
}
