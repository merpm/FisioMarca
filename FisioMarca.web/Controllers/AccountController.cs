using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FisioMarca.Data;
using FisioMarca.Helpers;
using FisioMarca.Models;
using FisioMarca.Models.viewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FisioMarca.Controllers
{
    public class AccountController : Controller
    {
        private readonly FisioMarcaDbContext _context;

        public AccountController(FisioMarcaDbContext context)
        {
            _context = context;
        }

        // =========================
        // HELPERS
        // =========================
        private int? SessionUserId => HttpContext.Session.GetInt32("USER_ID");

        private bool IsLogged()
            => SessionUserId.HasValue && SessionUserId.Value > 0;

        private async Task<User?> GetCurrentUserAsync()
        {
            if (!IsLogged()) return null;

            var id = SessionUserId!.Value;
            return await _context.Users.FirstOrDefaultAsync(u => u.Id == id && u.IsActive);
        }

        private static bool IsAdminUser(User user)
        {
            var role = (user.Role ?? "").Trim().ToLower();
            var email = (user.Email ?? "").Trim().ToLower();

            if (role == "admin" || role == "administrador")
                return true;

            if (email == "mary@fisiomarca.com" || email == "jonathan@fisiomarca.com")
                return true;

            return false;
        }

        private static object? ReadProp(object obj, params string[] names)
        {
            var t = obj.GetType();
            foreach (var name in names)
            {
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p != null) return p.GetValue(obj);
            }
            return null;
        }

        private static int? ReadInt(object obj, params string[] names)
        {
            var v = ReadProp(obj, names);
            if (v == null) return null;
            if (v is int i) return i;
            if (int.TryParse(v.ToString(), out var n)) return n;
            return null;
        }

        private static decimal ReadDecimal(object obj, params string[] names)
        {
            var v = ReadProp(obj, names);
            if (v == null) return 0m;
            if (v is decimal d) return d;
            if (decimal.TryParse(v.ToString(), out var n)) return n;
            return 0m;
        }

        private static DateTime ReadDateTime(object obj, params string[] names)
        {
            var v = ReadProp(obj, names);
            if (v == null) return DateTime.MinValue;
            if (v is DateTime dt) return dt;
            if (DateTime.TryParse(v.ToString(), out var n)) return n;
            return DateTime.MinValue;
        }

        private static string ReadString(object obj, params string[] names)
        {
            var v = ReadProp(obj, names);
            return v?.ToString() ?? "";
        }

        // =========================
        // LOGIN
        // =========================
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View(new LoginViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel vm, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
                return View(vm);

            var emailInput = (vm.Email ?? string.Empty).Trim().ToLower();
            var hash = PasswordHelper.Hash(vm.Password ?? string.Empty);

            var user = await _context.Users
                .FirstOrDefaultAsync(u =>
                    u.Email.ToLower() == emailInput &&
                    u.PasswordHash == hash &&
                    u.IsActive);

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Correo o contraseña incorrectos.");
                return View(vm);
            }

            // Guardar sesión
            HttpContext.Session.SetInt32("USER_ID", user.Id);
            HttpContext.Session.SetString("USER_NAME", user.FullName ?? string.Empty);
            HttpContext.Session.SetString("USER_ROLE", user.Role ?? string.Empty);
            HttpContext.Session.SetString("USER_EMAIL", user.Email ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            if (IsAdminUser(user))
                return RedirectToAction("Index", "Admin");

            return RedirectToAction("Panel", "Account");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View(new RegisterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            var email = (vm.Email ?? string.Empty).Trim().ToLower();

            bool exists = await _context.Users.AnyAsync(u => u.Email.ToLower() == email);
            if (exists)
            {
                ModelState.AddModelError(nameof(vm.Email), "Este correo ya está registrado.");
                return View(vm);
            }

            var client = new Client
            {
                FullName = (vm.FullName ?? string.Empty).Trim(),
                Email = email,
                Phone = ""
            };

            _context.Clients.Add(client);
            await _context.SaveChangesAsync();

            var user = new User
            {
                FullName = (vm.FullName ?? string.Empty).Trim(),
                Email = email,
                PasswordHash = PasswordHelper.Hash(vm.Password ?? string.Empty),
                Role = "Cliente",
                IsActive = true,
                ClientId = client.Id,
                CreatedAt = DateTime.Now
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            HttpContext.Session.SetInt32("USER_ID", user.Id);
            HttpContext.Session.SetString("USER_NAME", user.FullName ?? string.Empty);
            HttpContext.Session.SetString("USER_ROLE", user.Role ?? string.Empty);
            HttpContext.Session.SetString("USER_EMAIL", user.Email ?? string.Empty);

            return RedirectToAction("Panel", "Account");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public async Task<IActionResult> Panel(string? tab = "perfil")
        {
            if (!IsLogged())
                return RedirectToAction("Login", new { returnUrl = Url.Action("Panel", "Account") });

            var user = await GetCurrentUserAsync();
            if (user == null)
                return RedirectToAction("Login");

            // Si por alguna razón un admin entra aquí, lo mandamos al dashboard
            if (IsAdminUser(user))
                return RedirectToAction("Index", "Admin");

            var userName = HttpContext.Session.GetString("USER_NAME") ?? (user.FullName ?? "Usuario");
            var email = (HttpContext.Session.GetString("USER_EMAIL") ?? user.Email ?? "").Trim().ToLower();

            var activeTab = (tab ?? "perfil").Trim().ToLower();
            if (activeTab != "perfil" && activeTab != "reservas")
                activeTab = "perfil";

            // Traer client (para teléfono y datos de perfil)
            Client? client = null;
            if (user.ClientId.HasValue)
            {
                client = await _context.Clients
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == user.ClientId.Value);
            }

            // =========================
            // RESERVAS DEL CLIENTE
            // =========================
            List<Appointment> all;
            try
            {
                all = await _context.Appointments.ToListAsync();
            }
            catch
            {
                all = new List<Appointment>();
            }

            var clientId = user.ClientId;

            var my = all.Where(a =>
            {
                var aClientId = ReadInt(a, "ClientId", "clientId");
                var aUserId = ReadInt(a, "UserId", "userId");
                if (aClientId.HasValue && clientId.HasValue && aClientId.Value == clientId.Value) return true;
                if (aUserId.HasValue && aUserId.Value == user.Id) return true;
                return false;
            }).ToList();

            var serviceIds = my
                .Select(a => ReadInt(a, "ServiceId", "serviceId", "EspecialidadId", "especialidadId"))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var servicesById = new Dictionary<int, Service>();
            if (serviceIds.Count > 0)
            {
                try
                {
                    servicesById = await _context.Services
                        .Where(s => serviceIds.Contains(s.Id))
                        .ToDictionaryAsync(s => s.Id, s => s);
                }
                catch
                {
                    servicesById = new Dictionary<int, Service>();
                }
            }

            var reservasVm = my
                .Select(a =>
                {
                    var sid = ReadInt(a, "ServiceId", "serviceId", "EspecialidadId", "especialidadId") ?? 0;
                    servicesById.TryGetValue(sid, out var svc);

                    // ✅ FIX IMPORTANTE: incluir DateTimeStart (tu campo real)
                    var fechaHora = ReadDateTime(a,
                        "DateTimeStart", "dateTimeStart",
                        "FechaHora", "fechaHora",
                        "DateTime", "dateTime",
                        "ScheduledAt", "scheduledAt");

                    var estado = ReadString(a, "Estado", "estado", "Status", "status");
                    var comentario = ReadString(a, "Comentario", "comentario", "Comment", "comment", "Notes", "notes");

                    var espec = svc == null ? "" : ReadString(svc, "Nombre", "nombre", "Name", "name", "Titulo", "titulo");

                    // primero intenta precio desde cita, luego desde servicio
                    var precioCita = ReadDecimal(a, "ServicePrice", "servicePrice", "Precio", "precio", "Price", "price");
                    var precioServicio = svc == null ? 0m : ReadDecimal(svc, "Precio", "precio", "Price", "price", "Cost", "cost");
                    var precio = precioCita > 0 ? precioCita : precioServicio;

                    return new UserReservationItemVM
                    {
                        Id = a.Id,
                        FechaHora = fechaHora,
                        Especialidad = string.IsNullOrWhiteSpace(espec) ? "Servicio" : espec,
                        Precio = precio,
                        Estado = string.IsNullOrWhiteSpace(estado) ? "Programada" : estado,
                        Comentario = string.IsNullOrWhiteSpace(comentario) ? "-" : comentario
                    };
                })
                .OrderByDescending(x => x.FechaHora)
                .ToList();

            var vm = new UserPanelVM
            {
                UserName = userName,
                FullName = user.FullName ?? userName,
                Email = !string.IsNullOrWhiteSpace(client?.Email) ? client!.Email! : email,
                Phone = client?.Phone ?? "",
                ActiveTab = activeTab,
                Reservas = reservasVm
            };

            return View(vm);
        }
        
        [HttpGet]
        public IActionResult ChangePassword()
        {
            if (!IsLogged())
                return RedirectToAction("Login", new { returnUrl = Url.Action("ChangePassword", "Account") });

            return View(new ChangePasswordVM());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordVM vm)
        {
            if (!IsLogged())
                return RedirectToAction("Login");

            if (!ModelState.IsValid)
                return View(vm);

            var user = await GetCurrentUserAsync();
            if (user == null)
                return RedirectToAction("Login");

            var currentHash = PasswordHelper.Hash(vm.CurrentPassword ?? "");
            if (!string.Equals(user.PasswordHash, currentHash, StringComparison.Ordinal))
            {
                ModelState.AddModelError(string.Empty, "La contraseña actual es incorrecta.");
                return View(vm);
            }

            user.PasswordHash = PasswordHelper.Hash(vm.NewPassword ?? "");
            await _context.SaveChangesAsync();

            TempData["OK"] = "Contraseña actualizada correctamente.";
            return RedirectToAction("Panel");
        }
    }
}
