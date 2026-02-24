namespace FisioMarca.Models.ViewModels
{
    public class CalendarEventVM
    {
        public int Id { get; set; }
        public DateTime DateTimeStart { get; set; }
        public string Title { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string Status { get; set; } = "Programada";
    }
}
