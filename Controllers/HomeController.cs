using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SubdivisionManagement.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System.Text;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using System.ComponentModel.DataAnnotations;

namespace Subdivision_Management_System.Controllers;

public class HomeController : Controller
{
    private readonly HomeContext _context;
    private readonly ILogger<HomeController> _logger;
    private readonly IAntiforgery _antiforgery;

    public HomeController(HomeContext context, ILogger<HomeController> logger, IAntiforgery antiforgery)
    {
        _context = context;
        _logger = logger;
        _antiforgery = antiforgery;
    }

    public IActionResult Index()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        ViewBag.AntiForgeryToken = tokens.RequestToken;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password)
    { 
        var homeowner = await _context.Homeowners.FirstOrDefaultAsync(h => h.Email == email);
        
        if (homeowner != null && VerifyPassword(password, homeowner.PasswordHash))
        {
            if (homeowner.Status.ToLower() != "approved")
            {
                ViewBag.ShowErrorMessage = true;
                ViewBag.ErrorMessage = homeowner.Status.ToLower() == "pending" 
                    ? "Your account is pending approval. Please wait for admin approval."
                    : "Your account has been disapproved. Please contact the administrator.";
                return View("Index");
            }

            // Set session with proper configuration
            HttpContext.Session.SetInt32("UserId", homeowner.Id);
            HttpContext.Session.SetString("UserEmail", homeowner.Email);
            HttpContext.Session.SetString("UserName", $"{homeowner.FirstName} {homeowner.LastName}");
            
            // Set a session cookie
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.Now.AddHours(24)
            };
            Response.Cookies.Append("UserSession", homeowner.Id.ToString(), cookieOptions);

            return RedirectToAction("Dashboard");
        }

        ViewBag.ShowErrorMessage = true;
        ViewBag.ErrorMessage = "Invalid email or password. Please try again.";
        return View("Index");
    }

    private bool VerifyPassword(string password, string storedHash)
    {
        byte[] salt = Encoding.UTF8.GetBytes("password123");
        string hashedPassword = Convert.ToBase64String(KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: 10000,
            numBytesRequested: 256 / 8));
        return hashedPassword == storedHash;
    }

    public IActionResult Dashboard()
    {
        if (!IsUserLoggedIn())
        {
            return RedirectToAction("Index");
        }

        var loggedInUserId = GetLoggedInUserId();
        var announcements = _context.Announcements
            .Include(a => a.Staff)
            .OrderByDescending(a => a.DateCreated)
            .Take(4)
            .ToList();

        // Get user's todos
        var todos = _context.Todos
            .Where(t => t.HomeownerId == loggedInUserId)
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        ViewBag.Todos = todos;
        ViewBag.UserId = loggedInUserId;
            
        return View(announcements);
    }

    public IActionResult Contact()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitRequest([FromBody] ContactRequestDto request)
    {
        try
        {
            var loggedInUserId = GetLoggedInUserId();
            if (loggedInUserId == null)
            {
                return Json(new { success = false, message = "Please log in to submit a request." });
            }

            var contactRequest = new ContactRequest
            {
                HomeownerId = loggedInUserId.Value,
                QueryType = request.QueryType,
                Message = request.Message,
                Status = "Pending",
                DateSubmitted = DateTime.UtcNow
            };

            _context.ContactRequests.Add(contactRequest);
            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                message = "Your request has been submitted successfully."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting contact request");
            return Json(new { success = false, message = "An error occurred while submitting your request." });
        }
    }

    public class ContactRequestDto
    {
        [Required]
        public string QueryType { get; set; } = string.Empty;
        
        [Required]
        public string Message { get; set; } = string.Empty;
    }

    [HttpGet]
    public async Task<IActionResult> GetRequestHistory()
    {
        var loggedInUserId = GetLoggedInUserId();
        if (loggedInUserId == null)
        {
            return Json(new List<object>());
        }

        var requests = await _context.ContactRequests
            .Where(r => r.HomeownerId == loggedInUserId.Value)
            .OrderByDescending(r => r.DateSubmitted)
            .Select(r => new
            {
                r.Id,
                r.QueryType,
                r.Message,
                r.Status,
                DateSubmitted = r.DateSubmitted.ToString("yyyy-MM-dd"),
                r.StaffNotes
            })
            .ToListAsync();

        return Json(requests);
    }

    public IActionResult Announcements()
    {
        if (!IsUserLoggedIn()) return RedirectToAction("Index");

        var announcements = _context.Announcements
            .Include(a => a.Staff)
            .OrderByDescending(a => a.DateCreated)
            .ToList()
            .GroupBy(a => a.Type)
            .ToDictionary(g => g.Key, g => g.ToList());
            
        return View(announcements);
    }

    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(Homeowner homeowner)
    {
        if (ModelState.IsValid)
        {
            homeowner.PasswordHash = HashPassword(homeowner.PasswordHash);
            homeowner.Status = "pending";
            _context.Homeowners.Add(homeowner);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "You've Successfully Registered. Please wait until your registration is approved.";
            return RedirectToAction("Index", "Home");
        }
        return View(homeowner);
    }

    private string HashPassword(string password)
    {
        byte[] salt = Encoding.UTF8.GetBytes("password123");
        return Convert.ToBase64String(KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: 10000,
            numBytesRequested: 256 / 8));
    }

    private int? GetLoggedInUserId()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        var userSession = Request.Cookies["UserSession"];
        
        // Verify both session and cookie match
        if (userId.HasValue && !string.IsNullOrEmpty(userSession) && 
            userId.Value.ToString() == userSession)
        {
            return userId;
        }
        
        return null;
    }

    private bool IsUserLoggedIn()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        var userSession = Request.Cookies["UserSession"];
        
        // Check both session and cookie
        return userId.HasValue && !string.IsNullOrEmpty(userSession) && 
               userId.Value.ToString() == userSession;
    }

    public IActionResult Profile()
    {
        if (!IsUserLoggedIn()) return RedirectToAction("Index");

        var loggedInUserId = GetLoggedInUserId();
        if (loggedInUserId == null) return RedirectToAction("Index");

        var homeowner = _context.Homeowners.FirstOrDefault(h => h.Id == loggedInUserId.Value);
        if (homeowner == null)
        {
            return RedirectToAction("Index");
        }

        return View(homeowner);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        if (!IsUserLoggedIn()) return RedirectToAction("Index");

        if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword) || string.IsNullOrEmpty(confirmPassword))
        {
            TempData["ErrorMessage"] = "All fields are required.";
            return RedirectToAction("Profile");
        }

        if (newPassword != confirmPassword)
        {
            TempData["ErrorMessage"] = "New password and confirmation do not match.";
            return RedirectToAction("Profile");
        }

        var loggedInUserId = GetLoggedInUserId();
        var homeowner = await _context.Homeowners.FirstOrDefaultAsync(h => h.Id == loggedInUserId);

        if (homeowner == null)
        {
            return RedirectToAction("Index");
        }

        // Verify current password
        if (!VerifyPassword(currentPassword, homeowner.PasswordHash))
        {
            TempData["ErrorMessage"] = "Current password is incorrect.";
            return RedirectToAction("Profile");
        }

        // Update password
        homeowner.PasswordHash = HashPassword(newPassword);
        _context.Update(homeowner);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Password changed successfully!";
        return RedirectToAction("Profile");
    }

    public IActionResult EditProfile()
    {
        if (!IsUserLoggedIn()) return RedirectToAction("Index");
        
        var loggedInUserId = GetLoggedInUserId();
        if (loggedInUserId == null) return RedirectToAction("Index");
        
        var homeowner = _context.Homeowners.FirstOrDefault(h => h.Id == loggedInUserId.Value);
        if (homeowner == null)
        {
            return RedirectToAction("Index");
        }
        
        return View("edit_profile", homeowner);
    }
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(Homeowner model)
    {
        if (!IsUserLoggedIn()) return RedirectToAction("Index");
        
        var loggedInUserId = GetLoggedInUserId();
        if (loggedInUserId == null) return RedirectToAction("Index");
        
        var homeowner = await _context.Homeowners.FirstOrDefaultAsync(h => h.Id == loggedInUserId.Value);
        if (homeowner == null)
        {
            return RedirectToAction("Index");
        }
        
        // Only update specific fields, not the entire homeowner object
        homeowner.FirstName = model.FirstName;
        homeowner.MiddleName = model.MiddleName;
        homeowner.LastName = model.LastName;
        homeowner.Email = model.Email;
        homeowner.PhoneNumber = model.PhoneNumber;
        homeowner.Phase = model.Phase;
        homeowner.Block = model.Block;
        homeowner.HouseNumber = model.HouseNumber;
        homeowner.Street = model.Street;
        homeowner.EmergencyContactName = model.EmergencyContactName;
        homeowner.EmergencyContactNumber = model.EmergencyContactNumber;
        
        // Don't update critical fields like PasswordHash and Status
        
        try
        {
            _context.Update(homeowner);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Profile updated successfully!";
            return RedirectToAction("Profile");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile");
            TempData["ErrorMessage"] = "An error occurred while updating your profile. Please try again.";
            return View("edit_profile", model);
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    public IActionResult Billing()
    {
        if (!IsUserLoggedIn()) return RedirectToAction("Index");

        var loggedInUserId = GetLoggedInUserId();
        var currentDate = DateTime.Now;
    
        // Get total pending bills
        var totalPending = _context.Billings
            .Where(b => b.HomeownerId == loggedInUserId && b.Status == "Pending")
            .Sum(b => b.Amount);
    
        // Get nearest upcoming due date
        var upcomingDueDate = _context.Billings
            .Where(b => b.HomeownerId == loggedInUserId && b.Date >= currentDate)
            .OrderBy(b => b.Date)
            .Select(b => b.Date)
            .FirstOrDefault();
    
        // Get total amount for current month
        var currentMonthTotal = _context.Billings
            .Where(b => b.HomeownerId == loggedInUserId && 
                       b.Date.Month == currentDate.Month && 
                       b.Date.Year == currentDate.Year)
            .Sum(b => b.Amount);
    
        // Get all billings for the logged in user
        var billings = _context.Billings
            .Where(b => b.HomeownerId == loggedInUserId)
            .OrderByDescending(b => b.Date)
            .ToList();
    
        ViewBag.TotalPending = totalPending;
        ViewBag.MostRecentDueDate = upcomingDueDate;
        ViewBag.CurrentMonthTotal = currentMonthTotal;
        ViewBag.Billings = billings;
    
        return View();
    }

    [HttpGet]
    public IActionResult Services()
    {
        if (!IsUserLoggedIn()) return RedirectToAction("Index");
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        ViewBag.AntiForgeryToken = tokens.RequestToken;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetHomeownerServiceRequests()
    {
        var loggedInUserId = GetLoggedInUserId();
        if (loggedInUserId == null) return Unauthorized();

        var requests = await _context.ServiceRequests
            .Where(r => r.HomeownerId == loggedInUserId.Value)
            .OrderByDescending(r => r.DateSubmitted)
            .Select(r => new 
            {
                r.Id,
                RequestId = $"SR-{r.Id:D3}",
                r.ServiceType,
                r.Status,
                DateSubmitted = r.DateSubmitted.ToString("yyyy-MM-dd"),
                r.Description,
                r.Priority,
                DateCompleted = r.DateCompleted != null ? r.DateCompleted.Value.ToString("yyyy-MM-dd") : null,
                CompletionTime = r.DateCompleted != null ? (r.DateCompleted.Value - r.DateSubmitted).TotalDays.ToString("F1") + " days" : null,
                r.StaffNotes
            })
            .ToListAsync();

        return Json(requests);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestService([FromBody] ServiceRequestDto requestDto)
    {
        var loggedInUserId = GetLoggedInUserId();
        if (loggedInUserId == null) return Unauthorized();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var serviceRequest = new ServiceRequest
        {
            HomeownerId = loggedInUserId.Value,
            ServiceType = requestDto.ServiceType,
            Description = requestDto.Description,
            Priority = requestDto.Priority,
            Status = "Pending",
            DateSubmitted = DateTime.UtcNow
        };

        _context.ServiceRequests.Add(serviceRequest);
        await _context.SaveChangesAsync();

        return Json(new { 
            success = true, 
            message = "Service request submitted successfully!",
            request = new {
                Id = serviceRequest.Id,
                RequestId = $"SR-{serviceRequest.Id:D3}",
                serviceRequest.ServiceType,
                serviceRequest.Status,
                DateSubmitted = serviceRequest.DateSubmitted.ToString("yyyy-MM-dd"),
                serviceRequest.Description,
                serviceRequest.Priority,
                DateCompleted = (string?)null,
                CompletionTime = (string?)null,
                StaffNotes = (string?)null
            }
        });
    }

    public class ServiceRequestDto
    {
        [Required] public string ServiceType { get; set; } = string.Empty;
        [Required] public string Description { get; set; } = string.Empty;
        [Required] public string Priority { get; set; } = string.Empty;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestVisitorPass([FromBody] VisitorPassDto passDto)
    {
        try
        {
            var loggedInUserId = GetLoggedInUserId();
            if (loggedInUserId == null) 
            {
                return Unauthorized(new { success = false, message = "Please log in to continue." });
            }

            var visitorPass = new VisitorPass
            {
                HomeownerId = loggedInUserId.Value,
                VisitorName = passDto.VisitorName,
                VisitDate = DateTime.Parse(passDto.VisitDate),
                Purpose = passDto.Purpose,
                Status = "Pending",
                RequestDate = DateTime.UtcNow
            };

            _context.VisitorPasses.Add(visitorPass);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = "Visitor pass request submitted successfully!",
                pass = new
                {
                    visitorPass.Id,
                    visitorPass.VisitorName,
                    VisitDate = visitorPass.VisitDate.ToString("yyyy-MM-dd"),
                    visitorPass.Purpose,
                    visitorPass.Status
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting visitor pass request");
            return StatusCode(500, new { success = false, message = "An error occurred while submitting the request." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterVehicle([FromBody] VehicleRegistrationDto regDto)
    {
        try
        {
            var loggedInUserId = GetLoggedInUserId();
            if (loggedInUserId == null) 
            {
                return Unauthorized(new { success = false, message = "Please log in to continue." });
            }

            var registration = new VehicleRegistration
            {
                HomeownerId = loggedInUserId.Value,
                VehicleMake = regDto.VehicleMake,
                VehicleModel = regDto.VehicleModel,
                PlateNumber = regDto.PlateNumber,
                Status = "Pending",
                RegistrationDate = DateTime.UtcNow
            };

            _context.VehicleRegistrations.Add(registration);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = "Vehicle registration submitted successfully!",
                registration = new
                {
                    registration.Id,
                    registration.VehicleMake,
                    registration.VehicleModel,
                    registration.PlateNumber,
                    registration.Status
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting vehicle registration");
            return StatusCode(500, new { success = false, message = "An error occurred while submitting the registration." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetVisitorPasses()
    {
        try
        {
            var loggedInUserId = GetLoggedInUserId();
            if (loggedInUserId == null) return Unauthorized();

            var passes = await _context.VisitorPasses
                .Where(v => v.HomeownerId == loggedInUserId.Value)
                .OrderByDescending(v => v.RequestDate)
                .Select(v => new
                {
                    v.Id,
                    v.VisitorName,
                    VisitDate = v.VisitDate.ToString("yyyy-MM-dd"),
                    v.Purpose,
                    v.Status
                })
                .ToListAsync();

            return Json(passes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving visitor passes");
            return StatusCode(500, new { message = "An error occurred while retrieving visitor passes." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetVehicleRegistrations()
    {
        try
        {
            var loggedInUserId = GetLoggedInUserId();
            if (loggedInUserId == null) return Unauthorized();

            var registrations = await _context.VehicleRegistrations
                .Where(v => v.HomeownerId == loggedInUserId.Value)
                .OrderByDescending(v => v.RegistrationDate)
                .Select(v => new
                {
                    v.Id,
                    v.VehicleMake,
                    v.VehicleModel,
                    v.PlateNumber,
                    v.Status
                })
                .ToListAsync();

            return Json(registrations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving vehicle registrations");
            return StatusCode(500, new { message = "An error occurred while retrieving vehicle registrations." });
        }
    }

    public class VisitorPassDto
    {
        [Required]
        public string VisitorName { get; set; } = string.Empty;
        
        [Required]
        public string VisitDate { get; set; } = string.Empty;
        
        [Required]
        public string Purpose { get; set; } = string.Empty;
    }

    public class VehicleRegistrationDto
    {
        [Required]
        public string VehicleMake { get; set; } = string.Empty;
        
        [Required]
        public string VehicleModel { get; set; } = string.Empty;
        
        [Required]
        public string PlateNumber { get; set; } = string.Empty;
    }

    public IActionResult security_visitors()
    {
        if (!IsUserLoggedIn()) return RedirectToAction("Index");
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        ViewBag.AntiForgeryToken = tokens.RequestToken;
        return View();
    }

    public IActionResult Community_forum()
    {
        if (!IsUserLoggedIn()) return RedirectToAction("Index");
        return View();
    }

    public IActionResult facility_reservation()
    {
        if (!IsUserLoggedIn()) return RedirectToAction("Index");

        var facilities = _context.Facilities.ToList(); // Fetch facilities from the database
        return View(facilities); // Pass the facilities to the view
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BookFacility([FromForm] FacilityReservationDto reservationDto)
    {
        var loggedInUserId = GetLoggedInUserId();
        if (loggedInUserId == null) return Unauthorized();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var facilityReservation = new FacilityReservation
        {
            FacilityId = reservationDto.FacilityId,
            FacilityName = reservationDto.FacilityName,
            Purpose = reservationDto.Purpose,
            ReservationDate = reservationDto.ReservationDate,
            ReservationTimeStart = reservationDto.ReservationTimeStart,
            ReservationTimeEnd = reservationDto.ReservationTimeEnd,
            DateSubmitted = DateTime.UtcNow,
            Status = "Pending",
            HomeownerId = loggedInUserId.Value // Set HomeownerId from session
        };

        _context.FacilityReservations.Add(facilityReservation);
        await _context.SaveChangesAsync();

        return Json(new {
            success = true,
            message = "Facility reservation submitted successfully!"
        });
    }

    public class FacilityReservationDto
    {
        [Required] public int FacilityId { get; set; }
        [Required] public string FacilityName { get; set; } = string.Empty;
        [Required] public DateTime ReservationDate { get; set; }
        [Required] public TimeSpan ReservationTimeStart { get; set; }
        [Required] public TimeSpan ReservationTimeEnd { get; set; }
        [Required] public string Purpose { get; set; } = string.Empty;
    }

    [HttpGet]
    public async Task<IActionResult> GetHomeownerFacilityReservations()
    {
        var loggedInUserId = GetLoggedInUserId();
        if (loggedInUserId == null) return Unauthorized();
        var reservations = await _context.FacilityReservations
            .Where(r => r.HomeownerId == loggedInUserId.Value)
            .OrderByDescending(r => r.DateSubmitted)
            .Select(r => new {
                r.FacilityName,
                r.Purpose,
                ReservationDate = r.ReservationDate.ToString("yyyy-MM-dd"),
                ReservationTimeStart = r.ReservationTimeStart.ToString(),
                ReservationTimeEnd = r.ReservationTimeEnd.ToString(),
                r.Status,
                r.Note
            })
            .ToListAsync();
        return Json(reservations);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessPayment(int billingId, string paymentMethod)
    {
        var billing = await _context.Billings.FindAsync(billingId);
        if (billing == null)
        {
            return NotFound();
        }
    
        billing.Status = "Paid";
        billing.PaymentMethod = paymentMethod;
        billing.LastUpdatedBy = User.Identity?.Name ?? "System";
    
        _context.Billings.Update(billing);
        await _context.SaveChangesAsync();
    
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> GetServiceCategories()
    {
        try
        {
            var categories = await _context.ServiceCategories
                .OrderBy(c => c.Name)
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Description,
                    c.Icon
                })
                .ToListAsync();

            return Json(categories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching service categories");
            return StatusCode(500, new { message = "Error fetching service categories" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetTodos()
    {
        if (!IsUserLoggedIn()) return Unauthorized();

        var loggedInUserId = GetLoggedInUserId();
        var todos = await _context.Todos
            .Where(t => t.HomeownerId == loggedInUserId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return Json(new { success = true, todos });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTodo([FromBody] TodoDto todoDto)
    {
        if (!IsUserLoggedIn()) return Unauthorized();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var loggedInUserId = GetLoggedInUserId();
        var todo = new Todo
        {
            Title = todoDto.Title,
            Description = todoDto.Description,
            HomeownerId = loggedInUserId.Value,
            CreatedAt = DateTime.UtcNow
        };

        _context.Todos.Add(todo);
        await _context.SaveChangesAsync();

        return Json(new { success = true, todo });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTodoStatus([FromBody] UpdateTodoStatusDto dto)
    {
        if (!IsUserLoggedIn()) return Unauthorized();

        var todo = await _context.Todos.FindAsync(dto.TodoId);
        if (todo == null) return NotFound();

        var loggedInUserId = GetLoggedInUserId();
        if (todo.HomeownerId != loggedInUserId) return Unauthorized();

        todo.IsCompleted = dto.IsCompleted;
        await _context.SaveChangesAsync();

        return Json(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTodo([FromBody] DeleteTodoDto dto)
    {
        if (!IsUserLoggedIn()) return Unauthorized();

        var todo = await _context.Todos.FindAsync(dto.TodoId);
        if (todo == null) return NotFound();

        var loggedInUserId = GetLoggedInUserId();
        if (todo.HomeownerId != loggedInUserId) return Unauthorized();

        _context.Todos.Remove(todo);
        await _context.SaveChangesAsync();

        return Json(new { success = true });
    }

    public class TodoDto
    {
        [Required]
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class UpdateTodoStatusDto
    {
        [Required]
        public int TodoId { get; set; }
        [Required]
        public bool IsCompleted { get; set; }
    }

    public class DeleteTodoDto
    {
        [Required]
        public int TodoId { get; set; }
    }
}