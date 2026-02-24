using FisioMarca.Data;
using FisioMarca.Models;
using FisioMarca.Models.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace FisioMarca.Controllers
{
    public class AdminController : Controller
    {
        private readonly FisioMarcaDbContext _context;

        public AdminController(FisioMarcaDbContext context)
        {
            _context = context;
        }

        private bool IsAdmin()
        {
            var role = (HttpContext.Session.GetString("USER_ROLE") ?? "").Trim().ToLower();
            var email = (HttpContext.Session.GetString("USER_EMAIL") ?? "").Trim().ToLower();

            return role == "admin"
                || role == "administrador"
                || email == "mary@fisiomarca.com"
                || email == "jonathan@fisiomarca.com";
        }

        // Helper para cargar categorías en el combo del formulario
        // (usa ViewBag para no romper tu vista _ServiceForm actual)
        private async Task LoadServiceCategories(int? selectedCategoryId = null)
        {
            var categories = await _context.Categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();

            ViewBag.CategoryId = new SelectList(categories, "Id", "Name", selectedCategoryId);
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? range = "month", DateTime? from = null, DateTime? to = null)
        {
            if (!IsAdmin()) return RedirectToAction("Index", "Home");

            var now = DateTime.Now;
            DateTime start;
            DateTime end;

            switch ((range ?? "month").Trim().ToLower())
            {
                case "today":
                    start = now.Date;
                    end = start.AddDays(1);
                    break;

                case "week":
                    int diff = (7 + ((int)now.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
                    start = now.Date.AddDays(-diff);
                    end = start.AddDays(7);
                    break;

                case "custom":
                    start = (from ?? now.Date).Date;
                    end = (to ?? now.Date).Date.AddDays(1);
                    break;

                default:
                    start = new DateTime(now.Year, now.Month, 1);
                    end = start.AddMonths(1);
                    range = "month";
                    break;
            }

            var q = _context.Appointments
                .AsNoTracking()
                .Where(a => a.DateTimeStart >= start && a.DateTimeStart < end)
                .Include(a => a.Service)
                .Include(a => a.Client);

            var total = await q.CountAsync();
            var canceladas = await q.CountAsync(a => (a.Status ?? "").Trim().ToLower() == "cancelada");
            var atendidas = await q.CountAsync(a => (a.Status ?? "").Trim().ToLower() == "atendida");

            // null o vacío se considera Programada
            var programadas = await q.CountAsync(a =>
                string.IsNullOrWhiteSpace(a.Status) || (a.Status ?? "").Trim().ToLower() == "programada"
            );

            var tasaCancelacion = total == 0 ? 0 : (canceladas * 100.0 / total);

            var chartByDayRaw = await q
                .GroupBy(a => a.DateTimeStart.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(x => x.Date)
                .ToListAsync();

            var chartByDay = chartByDayRaw
                .Select(x => new ChartPointVM
                {
                    Label = x.Date.ToString("dd/MM"),
                    Value = x.Count
                })
                .ToList();

            var topServicesRaw = await q
                .Where(a => a.Service != null)
                .GroupBy(a => a.Service!.Name)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(6)
                .ToListAsync();

            var topServices = topServicesRaw
                .Select(x => new ChartPointVM
                {
                    Label = x.Name ?? "Sin nombre",
                    Value = x.Count
                })
                .ToList();

            var next24h = await _context.Appointments
                .AsNoTracking()
                .Where(a => a.DateTimeStart >= now && a.DateTimeStart <= now.AddHours(24))
                .Include(a => a.Client)
                .Include(a => a.Service)
                .OrderBy(a => a.DateTimeStart)
                .Take(12)
                .ToListAsync();

            var calStart = new DateTime(now.Year, now.Month, 1);
            var calEnd = calStart.AddMonths(1);

            var calendarItems = await _context.Appointments
                .AsNoTracking()
                .Where(a => a.DateTimeStart >= calStart && a.DateTimeStart < calEnd)
                .Include(a => a.Client)
                .Include(a => a.Service)
                .OrderBy(a => a.DateTimeStart)
                .Select(a => new CalendarEventVM
                {
                    Id = a.Id,
                    DateTimeStart = a.DateTimeStart,
                    Title = a.Service != null ? a.Service.Name : "Reserva",
                    ClientName = a.Client != null ? a.Client.FullName : "",
                    Status = a.Status ?? "Programada"
                })
                .ToListAsync();

            var vm = new AdminDashboardVM
            {
                Range = (range ?? "month").Trim().ToLower(),
                From = start.Date,
                To = end.AddDays(-1).Date,
                TotalReservas = total,
                Programadas = programadas,
                Atendidas = atendidas,
                Canceladas = canceladas,
                TasaCancelacion = Math.Round(tasaCancelacion, 2),
                ChartByDay = chartByDay,
                TopServices = topServices,
                Next24Hours = next24h,
                CalendarEvents = calendarItems
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Reservations(string? search = null, string? status = null)
        {
            if (!IsAdmin()) return RedirectToAction("Index", "Home");

            var query = _context.Appointments
                .AsNoTracking()
                .Include(a => a.Client)
                .Include(a => a.Service)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();

                query = query.Where(a =>
                    (a.Client != null && (a.Client.FullName ?? "").ToLower().Contains(s)) ||
                    (a.Service != null && (a.Service.Name ?? "").ToLower().Contains(s)) ||
                    ((a.Notes ?? "").ToLower().Contains(s))
                );
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                var st = status.Trim().ToLower();
                query = query.Where(a => ((a.Status ?? "programada").Trim().ToLower()) == st);
            }

            var list = await query
                .OrderByDescending(a => a.DateTimeStart)
                .ToListAsync();

            ViewBag.FilterSearch = search ?? "";
            ViewBag.FilterStatus = status ?? "";

            return View(list);
        }

        [HttpGet]
        public async Task<IActionResult> Services()
        {
            if (!IsAdmin()) return RedirectToAction("Index", "Home");

            var list = await _context.Services
                .AsNoTracking()
                .Include(s => s.Category)
                .OrderByDescending(s => s.IsActive)
                .ThenBy(s => s.Name)
                .ToListAsync();

            return View(list);
        }

        [HttpGet]
        public async Task<IActionResult> ServiceCreate()
        {
            if (!IsAdmin()) return RedirectToAction("Index", "Home");

            await LoadServiceCategories();

            return View("_ServiceForm", new ServiceFormVM
            {
                IsActive = true
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ServiceCreate(ServiceFormVM vm)
        {
            if (!IsAdmin()) return RedirectToAction("Index", "Home");

            if (!ModelState.IsValid)
            {
                await LoadServiceCategories(vm.CategoryId);
                return View("_ServiceForm", vm);
            }

            var entity = new Service
            {
                Name = (vm.Name ?? "").Trim(),
                Description = (vm.Description ?? "").Trim(),
                Price = vm.Price,
                ImageUrl = (vm.ImageUrl ?? "").Trim(),
                IsActive = vm.IsActive, // se mantiene
                CategoryId = vm.CategoryId
            };

            _context.Services.Add(entity);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Services));
        }

        [HttpGet]
        public async Task<IActionResult> ServiceEdit(int id)
        {
            if (!IsAdmin()) return RedirectToAction("Index", "Home");

            var s = await _context.Services.FindAsync(id);
            if (s == null) return NotFound();

            var vm = new ServiceFormVM
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                Price = s.Price,
                ImageUrl = s.ImageUrl,
                IsActive = s.IsActive, // se mantiene
                CategoryId = s.CategoryId
            };

            await LoadServiceCategories(vm.CategoryId);

            return View("_ServiceForm", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ServiceEdit(ServiceFormVM vm)
        {
            if (!IsAdmin()) return RedirectToAction("Index", "Home");

            if (!ModelState.IsValid)
            {
                await LoadServiceCategories(vm.CategoryId);
                return View("_ServiceForm", vm);
            }

            var s = await _context.Services.FindAsync(vm.Id);
            if (s == null) return NotFound();

            s.Name = (vm.Name ?? "").Trim();
            s.Description = (vm.Description ?? "").Trim();
            s.Price = vm.Price;
            s.ImageUrl = (vm.ImageUrl ?? "").Trim();
            s.IsActive = vm.IsActive; // se mantiene
            s.CategoryId = vm.CategoryId;

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Services));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ServiceToggleStatus(int id)
        {
            if (!IsAdmin()) return RedirectToAction("Index", "Home");

            var s = await _context.Services.FindAsync(id);
            if (s == null) return NotFound();

            s.IsActive = !s.IsActive;
            await _context.SaveChangesAsync();

            TempData["AdminMsg"] = s.IsActive
                ? $"La especialidad '{s.Name}' fue activada."
                : $"La especialidad '{s.Name}' fue desactivada.";

            return RedirectToAction(nameof(Services));
        }

        [HttpGet]
        public async Task<IActionResult> DashboardData(string? range = "month", DateTime? from = null, DateTime? to = null)
        {
            if (!IsAdmin()) return Unauthorized();

            var now = DateTime.Now;
            DateTime start;
            DateTime end;

            switch ((range ?? "month").Trim().ToLower())
            {
                case "today":
                    start = now.Date;
                    end = start.AddDays(1);
                    break;

                case "week":
                    int diff = (7 + ((int)now.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
                    start = now.Date.AddDays(-diff);
                    end = start.AddDays(7);
                    break;

                case "custom":
                    start = (from ?? now.Date).Date;
                    end = (to ?? now.Date).Date.AddDays(1);
                    break;

                default:
                    start = new DateTime(now.Year, now.Month, 1);
                    end = start.AddMonths(1);
                    range = "month";
                    break;
            }

            var baseQ = _context.Appointments
                .AsNoTracking()
                .Include(a => a.Client)
                .Include(a => a.Service);

            var q = baseQ.Where(a => a.DateTimeStart >= start && a.DateTimeStart < end);

            var total = await q.CountAsync();
            var canceladas = await q.CountAsync(a => (a.Status ?? "").Trim().ToLower() == "cancelada");
            var atendidas = await q.CountAsync(a => (a.Status ?? "").Trim().ToLower() == "atendida");

            var programadas = await q.CountAsync(a =>
                string.IsNullOrWhiteSpace(a.Status) || (a.Status ?? "").Trim().ToLower() == "programada"
            );

            var tasaCancelacion = total == 0 ? 0 : (canceladas * 100.0 / total);

            var calStart = new DateTime(now.Year, now.Month, 1);
            var calEnd = calStart.AddMonths(1);

            var events = await baseQ
                .Where(a => a.DateTimeStart >= calStart && a.DateTimeStart < calEnd)
                .OrderBy(a => a.DateTimeStart)
                .Select(a => new
                {
                    id = a.Id,
                    title = $"{a.DateTimeStart:HH:mm} {(a.Service != null ? a.Service.Name : "Reserva")}",
                    start = a.DateTimeStart.ToString("yyyy-MM-ddTHH:mm:ss"),
                    className = (a.Status ?? "programada").Trim().ToLower(),
                    extendedProps = new
                    {
                        service = a.Service != null ? a.Service.Name : "Reserva",
                        client = a.Client != null ? a.Client.FullName : "Cliente",
                        status = string.IsNullOrWhiteSpace(a.Status) ? "Programada" : a.Status!,
                        price = a.ServicePrice,
                        comment = a.Notes ?? ""
                    }
                })
                .ToListAsync();

            var byDayRaw = await q
                .GroupBy(a => a.DateTimeStart.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync();

            var totalDays = (end.Date - start.Date).Days;
            if (totalDays < 1) totalDays = 1;
            if (totalDays > 60) totalDays = 60;

            var days = Enumerable.Range(0, totalDays).Select(i => start.Date.AddDays(i)).ToList();
            var byDayLabels = days.Select(d => d.ToString("dd/MM")).ToArray();
            var byDayValues = days.Select(d => byDayRaw.FirstOrDefault(x => x.Day == d)?.Count ?? 0).ToArray();

            var top = await q
                .Where(a => a.Service != null)
                .GroupBy(a => a.Service!.Name)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            return Json(new
            {
                stats = new
                {
                    total,
                    programadas,
                    atendidas,
                    canceladas,
                    tasaCancelacion = Math.Round(tasaCancelacion, 2)
                },
                events,
                byDay = new { labels = byDayLabels, values = byDayValues },
                topServices = new
                {
                    labels = top.Select(x => x.Name).ToArray(),
                    values = top.Select(x => x.Count).ToArray()
                }
            });
        }

        [HttpGet]
        public async Task<IActionResult> ExportReservationsPdf(string? range = "month", DateTime? from = null, DateTime? to = null)
        {
            if (!IsAdmin()) return RedirectToAction("Index", "Home");

            var now = DateTime.Now;
            DateTime start;
            DateTime end;

            switch ((range ?? "month").Trim().ToLower())
            {
                case "today":
                    start = now.Date;
                    end = start.AddDays(1);
                    break;

                case "week":
                    int diff = (7 + ((int)now.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
                    start = now.Date.AddDays(-diff);
                    end = start.AddDays(7);
                    break;

                case "custom":
                    start = (from ?? now.Date).Date;
                    end = (to ?? now.Date).Date.AddDays(1);
                    break;

                default:
                    start = new DateTime(now.Year, now.Month, 1);
                    end = start.AddMonths(1);
                    break;
            }

            var list = await _context.Appointments
                .AsNoTracking()
                .Where(a => a.DateTimeStart >= start && a.DateTimeStart < end)
                .Include(a => a.Service)
                .Include(a => a.Client)
                .OrderBy(a => a.DateTimeStart)
                .ToListAsync();

            int total = list.Count;
            int canceladas = list.Count(a => (a.Status ?? "").Trim().ToLower() == "cancelada");
            int atendidas = list.Count(a => (a.Status ?? "").Trim().ToLower() == "atendida");
            int programadas = list.Count(a => string.IsNullOrWhiteSpace(a.Status) || (a.Status ?? "").Trim().ToLower() == "programada");

            decimal totalSoles = list.Sum(a => a.ServicePrice);

            var title = "Reporte de Reservas - FisioMarca";
            var rangoTxt = $"{start:dd/MM/yyyy} - {end.AddDays(-1):dd/MM/yyyy}";
            var fileName = $"reporte_reservas_{DateTime.Now:yyyyMMdd_HHmm}.pdf";

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.DefaultTextStyle(t => t.FontSize(10));

                    page.Header().Column(col =>
                    {
                        col.Item().Text(title).FontSize(16).SemiBold();
                        col.Item().Text($"Rango: {rangoTxt}").FontColor(Colors.Grey.Darken1);
                        col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    });

                    page.Content().Column(col =>
                    {
                        col.Item().PaddingTop(10).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Resumen").SemiBold().FontSize(12);
                                c.Item().Text($"Total reservas: {total}");
                                c.Item().Text($"Programadas: {programadas}");
                                c.Item().Text($"Atendidas: {atendidas}");
                                c.Item().Text($"Canceladas: {canceladas}");
                                c.Item().Text($"Total S/: {totalSoles:0.00}").SemiBold();
                            });

                            r.ConstantItem(220).AlignRight().Column(c =>
                            {
                                c.Item().Text($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}")
                                    .FontColor(Colors.Grey.Darken1);
                            });
                        });

                        col.Item().PaddingTop(14).Text("Detalle de reservas").SemiBold().FontSize(12);

                        col.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(85);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                                cols.ConstantColumn(55);
                                cols.ConstantColumn(70);
                                cols.RelativeColumn(2);
                            });

                            table.Header(h =>
                            {
                                h.Cell().Element(CellHeader).Text("Fecha");
                                h.Cell().Element(CellHeader).Text("Cliente");
                                h.Cell().Element(CellHeader).Text("Servicio");
                                h.Cell().Element(CellHeader).AlignRight().Text("S/");
                                h.Cell().Element(CellHeader).Text("Estado");
                                h.Cell().Element(CellHeader).Text("Notas");
                            });

                            foreach (var a in list)
                            {
                                var cliente = a.Client?.FullName ?? "-";
                                var servicio = a.Service?.Name ?? "-";
                                var estado = string.IsNullOrWhiteSpace(a.Status) ? "Programada" : a.Status!;
                                var notas = string.IsNullOrWhiteSpace(a.Notes) ? "-" : a.Notes!;

                                table.Cell().Element(CellBody).Text(a.DateTimeStart.ToString("dd/MM HH:mm"));
                                table.Cell().Element(CellBody).Text(cliente);
                                table.Cell().Element(CellBody).Text(servicio);
                                table.Cell().Element(CellBody).AlignRight().Text(a.ServicePrice.ToString("0.00", CultureInfo.InvariantCulture));
                                table.Cell().Element(CellBody).Text(estado);
                                table.Cell().Element(CellBody).Text(notas);
                            }
                        });
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("© 2026 FisioMarca • Reporte interno").FontColor(Colors.Grey.Darken1);
                    });
                });
            }).GeneratePdf();

            return File(pdfBytes, "application/pdf", fileName);

            static IContainer CellHeader(IContainer c) =>
                c.PaddingVertical(6).PaddingHorizontal(5)
                 .Background(Colors.Grey.Lighten3)
                 .Border(1).BorderColor(Colors.Grey.Lighten2)
                 .DefaultTextStyle(t => t.SemiBold().FontColor(Colors.Black));

            static IContainer CellBody(IContainer c) =>
                c.PaddingVertical(6).PaddingHorizontal(5)
                 .BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
        }
    }
}