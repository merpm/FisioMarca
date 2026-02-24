using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FisioMarca.Models
{
    [Table("appointments")]
    public class Appointment
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("client_id")]
        public int ClientId { get; set; }

        [Column("service_id")]
        public int ServiceId { get; set; }

        [Column("datetime_start")]
        public DateTime DateTimeStart { get; set; }

        [Column("service_price", TypeName = "decimal(10,2)")]
        public decimal ServicePrice { get; set; }

        [MaxLength(30)]
        [Column("status")]
        public string Status { get; set; } = "Programada";

        [MaxLength(500)]
        [Column("notes")]
        public string? Notes { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(ClientId))]
        public Client? Client { get; set; }

        [ForeignKey(nameof(ServiceId))]
        public Service? Service { get; set; }
    }
}
