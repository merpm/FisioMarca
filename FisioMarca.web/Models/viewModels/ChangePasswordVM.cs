using System.ComponentModel.DataAnnotations;

namespace FisioMarca.Models.viewModels
{
    public class ChangePasswordVM
    {
        [Required]
        public string CurrentPassword { get; set; } = "";

        [Required, MinLength(6)]
        public string NewPassword { get; set; } = "";

        [Required, Compare(nameof(NewPassword), ErrorMessage = "Las contraseñas no coinciden.")]
        public string ConfirmPassword { get; set; } = "";
    }
}
