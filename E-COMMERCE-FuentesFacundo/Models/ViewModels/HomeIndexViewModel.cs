using E_COMMERCE_FuentesFacundo.Models;
using E_COMMERCE_FuentesFacundo.Models.Entities;

namespace E_COMMERCE_FuentesFacundo.Models.ViewModels
{
    public class HomeIndexViewModel
    {
        public string? Busqueda { get; set; }
        public PaginatedList<Producto> Productos { get; set; } =
            new(new List<Producto>(), 0, 1, PaginatedList<Producto>.DefaultPageSize);

        public List<Producto> CarruselProductos { get; set; } = new();
    }
}
