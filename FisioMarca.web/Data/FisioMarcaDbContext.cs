using FisioMarca.Models;
using Microsoft.EntityFrameworkCore;

namespace FisioMarca.Data
{
    public class FisioMarcaDbContext : DbContext
    {
        public FisioMarcaDbContext(DbContextOptions<FisioMarcaDbContext> options) : base(options) { }

        public DbSet<Client> Clients => Set<Client>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<User> Users => Set<User>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<Category> Categories => Set<Category>(); 

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Service>()
                .HasOne(s => s.Category)
                .WithMany(c => c.Services)
                .HasForeignKey(s => s.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}