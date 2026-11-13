using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace E_COMMERCE_FuentesFacundo.Services
{
    public class CartLine
    {
        public int IdProducto { get; set; }
        public int Cantidad { get; set; }
    }

    public class CartService
    {
        private const string SessionKey = "cart";
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly IHttpContextAccessor _httpContextAccessor;

        public CartService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private ISession Session => _httpContextAccessor.HttpContext!.Session;

        public List<CartLine> GetLines()
        {
            var json = Session.GetString(SessionKey);
            if (string.IsNullOrEmpty(json))
            {
                return new List<CartLine>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<CartLine>>(json, JsonOptions) ?? new List<CartLine>();
            }
            catch (JsonException)
            {
                return new List<CartLine>();
            }
        }

        public void Add(int idProducto, int cantidad)
        {
            var lines = GetLines();
            var line = lines.FirstOrDefault(l => l.IdProducto == idProducto);
            if (line == null)
            {
                lines.Add(new CartLine { IdProducto = idProducto, Cantidad = cantidad });
            }
            else
            {
                line.Cantidad += cantidad;
            }

            Save(lines);
        }

        public void UpdateQty(int idProducto, int cantidad)
        {
            var lines = GetLines();
            var line = lines.FirstOrDefault(l => l.IdProducto == idProducto);
            if (line == null)
            {
                return;
            }

            if (cantidad <= 0)
            {
                lines.Remove(line);
            }
            else
            {
                line.Cantidad = cantidad;
            }

            Save(lines);
        }

        public void Remove(int idProducto)
        {
            var lines = GetLines();
            lines.RemoveAll(l => l.IdProducto == idProducto);
            Save(lines);
        }

        public void Clear()
        {
            Session.Remove(SessionKey);
        }

        public int TotalQuantity()
        {
            return GetLines().Sum(l => l.Cantidad);
        }

        private void Save(List<CartLine> lines)
        {
            Session.SetString(SessionKey, JsonSerializer.Serialize(lines));
        }
    }
}
