using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FisioMarca.Models.ViewModels
{
	public class AppointmentCreateVM
	{
		[Required(ErrorMessage = "Selecciona una fecha.")]
		[DataType(DataType.Date)]
		public DateTime? AppointmentDate { get; set; }

		[Required(ErrorMessage = "Selecciona una hora.")]
		public string? StartTime { get; set; } // Ej: "07:00"

		[Required(ErrorMessage = "Selecciona al menos una especialidad.")]
		public List<int> SelectedServiceIds { get; set; } = new();

		public decimal TotalPrice { get; set; }
		public int TotalDurationMinutes { get; set; }

		[MaxLength(500)]
		public string? Notes { get; set; }
	}
}