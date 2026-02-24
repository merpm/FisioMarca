using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using FisioMarca.Data;
using FisioMarca.Models;
using FisioMarca.Models.ViewModels;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FisioMarca.Controllers
{
    public class AppointmentsController : Controller
    {
        private readonly FisioMarcaDbContext _context;

        // ===== Configuración de agenda =====
        private const int SLOT_STEP_MINUTES = 30;          // 30 min (cámbialo a 60 si quieres solo horas exactas)
        private const int MAX_SIMULTANEAS_POR_TRAMO = 1;   // 1 = solo una reserva a la vez

        public AppointmentsController(FisioMarcaDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var userId = HttpContext.Session.GetInt32("USER_ID");
            if (userId == null) return RedirectToAction("Login", "Account");

            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId.Value && u.IsActive);

            if (user == null) return RedirectToAction("Login", "Account");
            if (user.ClientId == null) return View(new List<FisioMarca.Models.Appointment>());

            var myAppointments = await _context.Appointments
                                .AsNoTracking()
                                .Include(a => a.Service)
                                .Include(a => a.Client)
                                .Where(a => a.ClientId == user.ClientId.Value)
                                .Where(a => a.Status == null || a.Status.Trim().ToLower() != "cancelada")
                                .OrderByDescending(a => a.DateTimeStart)
                                .ToListAsync();

            return View(myAppointments);
        }

        [HttpGet]
        public async Task<IActionResult> Create(int? serviceId)
        {
            var userId = HttpContext.Session.GetInt32("USER_ID");
            if (!userId.HasValue)
            {
                var returnUrl = Url.Action("Create", "Appointments", new { serviceId });
                return RedirectToAction("Login", "Account", new { returnUrl });
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userId.Value && u.IsActive);

            if (user == null || user.ClientId == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            await LoadCombos(user.ClientId, serviceId);

            var vm = new AppointmentCreateVM
            {
                AppointmentDate = DateTime.Today,
                StartTime = "07:00"
            };

            if (serviceId.HasValue && serviceId.Value > 0)
                vm.SelectedServiceIds = new List<int> { serviceId.Value };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AppointmentCreateVM vm)
        {
            var userId = HttpContext.Session.GetInt32("USER_ID");
            if (!userId.HasValue)
                return RedirectToAction("Login", "Account");

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userId.Value && u.IsActive);

            if (user == null || user.ClientId == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            // Normalizar IDs seleccionados
            var requestedIds = (vm.SelectedServiceIds ?? new List<int>())
                .Where(x => x > 0)
                .ToList();

            // Validaciones básicas
            if (vm.AppointmentDate == null)
                ModelState.AddModelError(nameof(vm.AppointmentDate), "Selecciona una fecha.");

            if (string.IsNullOrWhiteSpace(vm.StartTime) || !TimeSpan.TryParse(vm.StartTime, out var startTime))
                ModelState.AddModelError(nameof(vm.StartTime), "Selecciona una hora válida.");

            if (!requestedIds.Any())
                ModelState.AddModelError(nameof(vm.SelectedServiceIds), "Selecciona al menos una especialidad.");

            // Cargar servicios activos seleccionados
            var selectedServices = await _context.Services
                .AsNoTracking()
                .Where(s => requestedIds.Contains(s.Id) && s.IsActive)
                .ToListAsync();

            if (selectedServices.Count != requestedIds.Distinct().Count())
                ModelState.AddModelError(nameof(vm.SelectedServiceIds), "Uno o más servicios no son válidos o no están disponibles.");

            // Preservar orden del multiselect
            var orderedServices = requestedIds
                .Distinct()
                .Select(id => selectedServices.FirstOrDefault(s => s.Id == id))
                .Where(s => s != null)
                .Cast<Service>()
                .ToList();

            var totalDuration = orderedServices.Sum(s => s.DurationMinutes <= 0 ? 45 : s.DurationMinutes);
            var totalPrice = orderedServices.Sum(s => s.Price);

            vm.TotalDurationMinutes = totalDuration;
            vm.TotalPrice = totalPrice;
            vm.SelectedServiceIds = requestedIds; // por si vuelve a la vista con errores

            if (ModelState.IsValid && vm.AppointmentDate != null && TimeSpan.TryParse(vm.StartTime, out startTime))
            {
                var startDateTime = vm.AppointmentDate.Value.Date.Add(startTime);

                // No permitir reservar en el pasado
                if (startDateTime < DateTime.Now)
                {
                    ModelState.AddModelError(nameof(vm.StartTime), "No puedes reservar en una hora pasada.");
                }
                else
                {
                    // Validar bloque completo (duración total de todos los servicios)
                    var validation = await ValidateAvailabilityAsync(startDateTime, totalDuration);
                    if (!validation.IsAvailable)
                    {
                        ModelState.AddModelError(nameof(vm.StartTime), validation.Message ?? "La hora seleccionada ya no está disponible.");
                    }
                    else
                    {
                        // Crear citas consecutivas (una por servicio)
                        using var tx = await _context.Database.BeginTransactionAsync();
                        try
                        {
                            // Re-validación dentro de transacción (evita colisiones)
                            var recheck = await ValidateAvailabilityAsync(startDateTime, totalDuration);
                            if (!recheck.IsAvailable)
                            {
                                ModelState.AddModelError(nameof(vm.StartTime), recheck.Message ?? "La hora seleccionada ya fue tomada por otro cliente.");
                            }
                            else
                            {
                                var cursor = startDateTime;

                                foreach (var service in orderedServices)
                                {
                                    var duration = service.DurationMinutes <= 0 ? 45 : service.DurationMinutes;

                                    var appointment = new Appointment
                                    {
                                        ClientId = user.ClientId.Value,
                                        ServiceId = service.Id,
                                        DateTimeStart = cursor,
                                        ServicePrice = service.Price,
                                        Status = "Programada",
                                        Notes = vm.Notes
                                    };

                                    _context.Appointments.Add(appointment);

                                    // Siguiente cita consecutiva
                                    cursor = cursor.AddMinutes(duration);
                                }

                                await _context.SaveChangesAsync();
                                await tx.CommitAsync();

                                return RedirectToAction("Panel", "Account", new { tab = "reservas" });
                            }
                        }
                        catch
                        {
                            await tx.RollbackAsync();
                            ModelState.AddModelError(string.Empty, "Ocurrió un error al registrar la reserva.");
                        }
                    }
                }
            }

            await LoadCombos(user.ClientId, vm.SelectedServiceIds.FirstOrDefault());
            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var userId = HttpContext.Session.GetInt32("USER_ID");
            if (!userId.HasValue)
                return RedirectToAction("Login", "Account", new { returnUrl = Url.Action("Edit", "Appointments", new { id }) });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value && u.IsActive);
            if (user == null || user.ClientId == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            var appointment = await _context.Appointments
                .FirstOrDefaultAsync(a => a.Id == id && a.ClientId == user.ClientId.Value);

            if (appointment == null) return NotFound();

            if (!string.Equals(appointment.Status, "Programada", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Panel", "Account", new { tab = "reservas" });

            await LoadCombos(user.ClientId, appointment.ServiceId);
            return View(appointment);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,DateTimeStart,ServiceId,Notes")] Appointment vm)
        {
            var userId = HttpContext.Session.GetInt32("USER_ID");
            if (!userId.HasValue)
                return RedirectToAction("Login", "Account");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value && u.IsActive);
            if (user == null || user.ClientId == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            var appointment = await _context.Appointments
                .FirstOrDefaultAsync(a => a.Id == id && a.ClientId == user.ClientId.Value);

            if (appointment == null) return NotFound();

            if (!string.Equals(appointment.Status, "Programada", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Panel", "Account", new { tab = "reservas" });

            var service = await _context.Services
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == vm.ServiceId && s.IsActive);

            if (service == null)
                ModelState.AddModelError(nameof(vm.ServiceId), "Especialidad no válida o no disponible.");

            if (vm.DateTimeStart < DateTime.Now)
                ModelState.AddModelError(nameof(vm.DateTimeStart), "No puedes reprogramar a una fecha/hora pasada.");

            if (service != null)
            {
                var duration = service.DurationMinutes <= 0 ? 45 : service.DurationMinutes;
                var validation = await ValidateAvailabilityAsync(vm.DateTimeStart, duration, appointment.Id);

                if (!validation.IsAvailable)
                    ModelState.AddModelError(nameof(vm.DateTimeStart), validation.Message ?? "La hora seleccionada no está disponible.");
            }

            if (!ModelState.IsValid)
            {
                await LoadCombos(user.ClientId, vm.ServiceId);
                vm.ClientId = appointment.ClientId;
                vm.Status = appointment.Status;
                vm.ServicePrice = appointment.ServicePrice;
                return View(vm);
            }

            appointment.DateTimeStart = vm.DateTimeStart;
            appointment.ServiceId = vm.ServiceId;
            appointment.ServicePrice = service!.Price;
            appointment.Notes = vm.Notes;

            await _context.SaveChangesAsync();
            return RedirectToAction("Panel", "Account", new { tab = "reservas" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var userId = HttpContext.Session.GetInt32("USER_ID");
            if (!userId.HasValue)
                return RedirectToAction("Login", "Account");

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userId.Value && u.IsActive);

            if (user == null || user.ClientId == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            var appointment = await _context.Appointments
                .FirstOrDefaultAsync(a => a.Id == id && a.ClientId == user.ClientId.Value);

            if (appointment == null)
                return NotFound();

            if (!string.Equals(appointment.Status, "Programada", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Panel", "Account", new { tab = "reservas" });

            appointment.Status = "Cancelada";
            await _context.SaveChangesAsync();

            return RedirectToAction("Panel", "Account", new { tab = "reservas" });
        }

        [HttpGet]
        public async Task<IActionResult> GetAvailableHours(DateTime? date, [FromQuery] List<int> serviceIds)
        {
            if (date == null)
                return Json(new { ok = false, message = "Fecha inválida." });

            serviceIds ??= new List<int>();

            var selectedServices = await _context.Services
                .AsNoTracking()
                .Where(s => serviceIds.Contains(s.Id) && s.IsActive)
                .ToListAsync();

            // Si no seleccionó servicios aún, mostramos slots con duración base (30)
            var totalDuration = selectedServices.Any()
                ? selectedServices.Sum(s => s.DurationMinutes <= 0 ? 45 : s.DurationMinutes)
                : 30;

            var slots = await BuildSlotsForDateAsync(date.Value.Date, totalDuration);

            return Json(new
            {
                ok = true,
                duration = totalDuration,
                slots = slots.Select(s => new
                {
                    value = s.TimeText,
                    label = s.TimeText,
                    available = s.IsAvailable,
                    reason = s.Reason
                })
            });
        }

        private async Task LoadCombos(int? selectedClient = null, int? selectedService = null)
        {
            var clients = await _context.Clients
                .AsNoTracking()
                .OrderBy(c => c.FullName)
                .ToListAsync();

            var services = await _context.Services
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .ToListAsync();

            ViewBag.ClientId = new SelectList(clients, "Id", "FullName", selectedClient);
            ViewBag.ServiceId = new SelectList(services, "Id", "Name", selectedService);
            ViewBag.Services = services; // <- usado por el multiselect en la vista
        }

        // ==========================================
        // Helpers de disponibilidad / slots
        // ==========================================
        private class SlotOption
        {
            public string TimeText { get; set; } = "";
            public bool IsAvailable { get; set; }
            public string? Reason { get; set; }
        }

        private class AvailabilityValidation
        {
            public bool IsAvailable { get; set; }
            public string? Message { get; set; }
        }

        private async Task<List<SlotOption>> BuildSlotsForDateAsync(DateTime day, int requestedDurationMinutes)
        {
            var result = new List<SlotOption>();

            // Horarios de atención:
            // 07:00 - 12:00
            // 14:00 - 19:00
            var segments = new List<(TimeSpan Start, TimeSpan End)>
            {
                (new TimeSpan(7, 0, 0),  new TimeSpan(12, 0, 0)),
                (new TimeSpan(14, 0, 0), new TimeSpan(19, 0, 0))
            };

            var dayStart = day.Date;
            var dayEnd = day.Date.AddDays(1);

            var existingAppointments = await _context.Appointments
                .AsNoTracking()
                .Include(a => a.Service)
                .Where(a => a.DateTimeStart >= dayStart && a.DateTimeStart < dayEnd)
                .Where(a => a.Status == null || a.Status.Trim().ToLower() != "cancelada")
                .ToListAsync();

            var existingIntervals = existingAppointments.Select(a =>
            {
                var duration = a.Service?.DurationMinutes ?? 45;
                if (duration <= 0) duration = 45;

                var start = a.DateTimeStart;
                var end = a.DateTimeStart.AddMinutes(duration);

                return new { Start = start, End = end };
            }).ToList();

            foreach (var seg in segments)
            {
                var cursor = seg.Start;
                while (cursor < seg.End)
                {
                    var slotStart = day.Date.Add(cursor);
                    var slotEnd = slotStart.AddMinutes(requestedDurationMinutes);

                    bool fitsSegment = slotEnd.TimeOfDay <= seg.End;
                    bool isPast = slotStart < DateTime.Now;

                    int overlapCount = existingIntervals.Count(e =>
                        slotStart < e.End && slotEnd > e.Start
                    );

                    bool isAvailable = fitsSegment && !isPast && overlapCount < MAX_SIMULTANEAS_POR_TRAMO;

                    string? reason = null;
                    if (!fitsSegment) reason = "No alcanza el tiempo en este turno";
                    else if (isPast) reason = "Hora pasada";
                    else if (overlapCount >= MAX_SIMULTANEAS_POR_TRAMO) reason = "Hora ocupada";

                    result.Add(new SlotOption
                    {
                        TimeText = slotStart.ToString("HH:mm"),
                        IsAvailable = isAvailable,
                        Reason = reason
                    });

                    cursor = cursor.Add(TimeSpan.FromMinutes(SLOT_STEP_MINUTES));
                }
            }

            return result;
        }

        private async Task<AvailabilityValidation> ValidateAvailabilityAsync(DateTime startDateTime, int requestedDurationMinutes, int? ignoreAppointmentId = null)
        {
            // Duración inválida
            if (requestedDurationMinutes <= 0)
            {
                return new AvailabilityValidation
                {
                    IsAvailable = false,
                    Message = "La duración de la cita es inválida."
                };
            }

            // Validar contra horario de atención
            if (!FitsBusinessHours(startDateTime, requestedDurationMinutes))
            {
                return new AvailabilityValidation
                {
                    IsAvailable = false,
                    Message = "La reserva no entra en el horario de atención (07:00–12:00 / 14:00–19:00)."
                };
            }

            var endDateTime = startDateTime.AddMinutes(requestedDurationMinutes);

            var dayStart = startDateTime.Date;
            var dayEnd = dayStart.AddDays(1);

            var query = _context.Appointments
                .AsNoTracking()
                .Include(a => a.Service)
                .Where(a => a.DateTimeStart >= dayStart && a.DateTimeStart < dayEnd)
                .Where(a => a.Status == null || a.Status.Trim().ToLower() != "cancelada");

            if (ignoreAppointmentId.HasValue)
                query = query.Where(a => a.Id != ignoreAppointmentId.Value);

            var existingAppointments = await query.ToListAsync();

            int overlapCount = existingAppointments.Count(a =>
            {
                var duration = a.Service?.DurationMinutes ?? 45;
                if (duration <= 0) duration = 45;

                var existingStart = a.DateTimeStart;
                var existingEnd = a.DateTimeStart.AddMinutes(duration);

                return startDateTime < existingEnd && endDateTime > existingStart;
            });

            if (overlapCount >= MAX_SIMULTANEAS_POR_TRAMO)
            {
                return new AvailabilityValidation
                {
                    IsAvailable = false,
                    Message = "La hora seleccionada ya está ocupada o copada."
                };
            }

            return new AvailabilityValidation { IsAvailable = true };
        }

        private bool FitsBusinessHours(DateTime start, int durationMinutes)
        {
            var end = start.AddMinutes(durationMinutes);

            // no permitimos pasar al día siguiente
            if (start.Date != end.Date) return false;

            var t1Start = new TimeSpan(7, 0, 0);
            var t1End = new TimeSpan(12, 0, 0);

            var t2Start = new TimeSpan(14, 0, 0);
            var t2End = new TimeSpan(19, 0, 0);

            var s = start.TimeOfDay;
            var e = end.TimeOfDay;

            bool inMorning = s >= t1Start && e <= t1End;
            bool inAfternoon = s >= t2Start && e <= t2End;

            return inMorning || inAfternoon;
        }
    }
}