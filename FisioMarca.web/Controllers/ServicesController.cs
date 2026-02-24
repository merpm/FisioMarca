using System.Globalization;
using System.Text;
using FisioMarca.Data;
using FisioMarca.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FisioMarca.Controllers
{
    public class ServicesController : Controller
    {
        private readonly FisioMarcaDbContext _context;

        public ServicesController(FisioMarcaDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string? q, decimal? maxPrice, string? category)
        {
            var cap = maxPrice ?? 300m;
            if (cap < 0) cap = 0;
            if (cap > 300) cap = 300;

            static string Norm(string input)
            {
                if (string.IsNullOrWhiteSpace(input)) return "";
                var formD = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
                var sb = new StringBuilder();

                foreach (var ch in formD)
                {
                    if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                        sb.Append(ch);
                }

                return sb.ToString().Normalize(NormalizationForm.FormC);
            }

            const string MY_COLLATION = "utf8mb4_0900_ai_ci";

            // ✅ Categorías reales desde la BD
            var categories = await _context.Categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => c.Name)
                .ToListAsync();

            // ✅ Query base (sin map manual)
            // No usamos Include porque proyectaremos directo a ViewModel (EF genera JOIN cuando lo necesita)
            var query = _context.Services
                .AsNoTracking()
                .Where(s => s.IsActive)
                .Where(s => s.Price <= cap)
                .AsQueryable();

            // ✅ Filtro por categoría real (nombre de la tabla categories)
            if (!string.IsNullOrWhiteSpace(category))
            {
                var cat = category.Trim();
                query = query.Where(s => s.Category != null && s.Category.Name == cat);
            }

            // ✅ Búsqueda por nombre/descripcion/categoría
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                var pattern = $"%{term}%";
                var termNorm = Norm(term);

                // soporte para búsqueda flexible por nombre de categoría (sin tildes)
                var matchedCategoryNames = categories
                    .Where(c => Norm(c).Contains(termNorm))
                    .ToList();

                if (matchedCategoryNames.Count > 0)
                {
                    query = query.Where(s =>
                        EF.Functions.Like(EF.Functions.Collate(s.Name, MY_COLLATION), pattern) ||
                        EF.Functions.Like(EF.Functions.Collate(s.Description ?? "", MY_COLLATION), pattern) ||
                        (s.Category != null && matchedCategoryNames.Contains(s.Category.Name))
                    );
                }
                else
                {
                    query = query.Where(s =>
                        EF.Functions.Like(EF.Functions.Collate(s.Name, MY_COLLATION), pattern) ||
                        EF.Functions.Like(EF.Functions.Collate(s.Description ?? "", MY_COLLATION), pattern) ||
                        (s.Category != null && EF.Functions.Like(EF.Functions.Collate(s.Category.Name, MY_COLLATION), pattern))
                    );
                }
            }

            // ✅ Proyección directa al VM (más eficiente que ToList + Select en memoria)
            var servicesVm = await query
                .OrderBy(s => s.Name)
                .Select(s => new ServiceCatalogVM.ServiceItemVM
                {
                    Id = s.Id,
                    Name = s.Name,
                    Description = s.Description,
                    Price = s.Price,
                    DurationMinutes = s.DurationMinutes,
                    ImageUrl = s.ImageUrl,
                    CategoryName = s.Category != null ? s.Category.Name : "Sin categoría"
                })
                .ToListAsync();

            var vm = new ServiceCatalogVM
            {
                Q = q,
                MaxPrice = cap,
                Category = category,
                Categories = categories,
                Services = servicesVm
            };

            return View(vm);
        }
    }
}