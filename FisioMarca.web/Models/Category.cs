using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FisioMarca.Models
{
    public class Category
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public ICollection<Service> Services { get; set; } = new List<Service>();
    }
}