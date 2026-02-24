using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FisioMarca.Models
{
    [Table("categories")] // <- IMPORTANTE (tabla real en MySQL)
    public class Category
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [StringLength(255)]
        [Column("description")]
        public string? Description { get; set; }

        [StringLength(500)]
        [Column("image_url")]
        public string? ImageUrl { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        public ICollection<Service> Services { get; set; } = new List<Service>();
    }
}