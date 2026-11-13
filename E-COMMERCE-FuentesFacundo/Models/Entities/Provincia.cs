using System.ComponentModel.DataAnnotations;

namespace E_COMMERCE_FuentesFacundo.Models.Entities
{
    public class Provincia
    {
        [Key]
        public int IdProvincia { get; set; }

        [Required]
        [MaxLength(100)]
        public string Nombre { get; set; } = string.Empty;

        public virtual ICollection<Direccion> Direcciones { get; set; } = new List<Direccion>();
    }
}
