using System.Collections.Generic;

namespace FisioMarca.Models.ViewModels
{
    public class ServiceCatalogVM
    {
        public string? Q { get; set; }
        public decimal MaxPrice { get; set; } = 300m;
        public string? Category { get; set; }

        public List<string> Categories { get; set; } = new();
        public List<ServiceItemVM> Services { get; set; } = new();

        public class ServiceItemVM
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string? Description { get; set; }
            public decimal Price { get; set; }
            public int DurationMinutes { get; set; }
            public string? ImageUrl { get; set; }
            public string CategoryName { get; set; } = "";
        }
    }
}
