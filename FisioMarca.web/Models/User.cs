using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FisioMarca.Models
{
    [Table("users")]
    public class User
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required, MaxLength(120)]
        [Column("full_name")]
        public string FullName { get; set; } = string.Empty;

        [Required, MaxLength(120)]
        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(256)]
        [Column("password_hash")]
        public string PasswordHash { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        [Column("role")]
        public string Role { get; set; } = "Cliente";

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("client_id")]
        public int? ClientId { get; set; }

        [ForeignKey(nameof(ClientId))]
        public Client? Client { get; set; }
    }
}
