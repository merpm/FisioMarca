using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FisioMarca.Models.ViewModels
{
    public class ServiceFormVM
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio")]
        [Display(Name = "Nombre")]
        public string Name { get; set; } = "";

        [Display(Name = "Descripción")]
        public string? Description { get; set; }

        [Range(0.01, 9999, ErrorMessage = "Precio no válido")]
        [Display(Name = "Precio")]
        public decimal Price { get; set; }

        [Display(Name = "Imagen URL")]
        public string? ImageUrl { get; set; }

        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true; // ✅ ESTA LÍNEA ES LA CLAVE

        [Display(Name = "Categoría")]
        public int? CategoryId { get; set; }

        public List<SelectListItem> CategoryOptions { get; set; } = new();
    }
}