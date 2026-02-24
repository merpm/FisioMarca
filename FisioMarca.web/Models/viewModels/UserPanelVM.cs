using System;
using System.Collections.Generic;

namespace FisioMarca.Models.viewModels
{
    public class UserPanelVM
    {
        public string UserName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string ActiveTab { get; set; } = "perfil"; 

        public List<UserReservationItemVM> Reservas { get; set; } = new();
    }

    public class UserReservationItemVM
    {
        public int Id { get; set; }
        public DateTime FechaHora { get; set; }
        public string Especialidad { get; set; } = "";
        public decimal Precio { get; set; }
        public string Estado { get; set; } = "";
        public string? Comentario { get; set; }
    }
}