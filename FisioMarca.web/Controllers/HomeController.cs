using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FisioMarca.Data;

namespace FisioMarca.Controllers
{
    public class HomeController : Controller
    {
        private readonly FisioMarcaDbContext _context;

        public HomeController(FisioMarcaDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var services = await _context.Services
                .AsNoTracking()
                .Include(s => s.Category) // ✅ IMPORTANTE
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .ToListAsync();

            return View(services);
        }

        public IActionResult Nosotros()
        {
            return View();
        }

        public IActionResult Error()
        {
            return View();
        }
    }
}