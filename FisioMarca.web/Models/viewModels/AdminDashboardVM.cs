using FisioMarca.Models;

namespace FisioMarca.Models.ViewModels
{
    public class AdminDashboardVM
    {
        public string Range { get; set; } = "month";
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        public int TotalReservas { get; set; }
        public int Programadas { get; set; }
        public int Atendidas { get; set; }
        public int Canceladas { get; set; }
        public double TasaCancelacion { get; set; }

        public List<ChartPointVM> ChartByDay { get; set; } = new();
        public List<ChartPointVM> TopServices { get; set; } = new();

        public List<Appointment> Next24Hours { get; set; } = new();
        public List<CalendarEventVM> CalendarEvents { get; set; } = new();
    }
}
