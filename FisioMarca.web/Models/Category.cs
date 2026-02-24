using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FisioMarca.Models
{
    [Table("categories")]
    public class Category
    {
        [Key]
        [Column("Id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("Name")]
        public string Name { get; set; } = string.Empty;

        [Column("Description")]
        public string? Description { get; set; }

        [Column("ImageUrl")]
        public string? ImageUrl { get; set; }

        [Column("IsActive")]
        public bool IsActive { get; set; } = true;

        public virtual ICollection<Service> Services { get; set; } = new List<Service>();
    }
}