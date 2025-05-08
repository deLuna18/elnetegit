using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using SubdivisionManagement.Model;
using System.Linq;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Antiforgery;


public class AdminController : Controller
{
    private readonly HomeContext _context;
    private readonly ILogger<AdminController> _logger;
    private readonly IAntiforgery _antiforgery;

    public AdminController(HomeContext context, ILogger<AdminController> logger, IAntiforgery antiforgery)
    {
        _context = context;
        _logger = logger;
        _antiforgery = antiforgery;
    }

    public IActionResult Login()
    {
        return View();
    }

    [HttpPost]
    public IActionResult Login(string username, string password)
    {
        var admin = _context.Admins.FirstOrDefault(a => a.Username == username);


        if (admin == null || !VerifyPassword(password, admin.PasswordHash))
        {
            ViewBag.Error = "Invalid username or password";
            return View();
        }

        HttpContext.Session.SetString("AdminUser", username);
        return RedirectToAction("Dashboard");
    }

    public IActionResult Dashboard()
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
        {
            return RedirectToAction("Login");
        }

        // Get dashboard statistics
        var totalHouses = _context.Homeowners.Select(h => new { h.Block, h.HouseNumber }).Distinct().Count();
        var totalResidents = _context.Homeowners.Count();
        var totalIncome = _context.Billings.Where(b => b.Status == "Paid").Sum(b => b.Amount);
        var totalEmployees = _context.Staffs.Count();

        // Find the most recent year with paid billings
        var latestYear = _context.Billings
            .Where(b => b.Status == "Paid")
            .OrderByDescending(b => b.Date)
            .Select(b => b.Date.Year)
            .FirstOrDefault();

        // Get monthly income data for the latest year
        var monthlyIncome = _context.Billings
            .Where(b => b.Status == "Paid" && b.Date.Year == latestYear)
            .GroupBy(b => b.Date.Month)
            .Select(g => new { Month = g.Key, Total = g.Sum(b => b.Amount) })
            .OrderBy(x => x.Month)
            .ToList();

        // Get house distribution by block
        var houseDistribution = _context.Homeowners
            .GroupBy(h => h.Block)
            .Select(g => new { Block = g.Key, Count = g.Select(h => h.HouseNumber).Distinct().Count() })
            .OrderBy(x => x.Block)
            .ToList();

        ViewBag.TotalHouses = totalHouses;
        ViewBag.TotalResidents = totalResidents;
        ViewBag.TotalIncome = totalIncome;
        ViewBag.TotalEmployees = totalEmployees;
        ViewBag.MonthlyIncome = monthlyIncome;
        ViewBag.HouseDistribution = houseDistribution;
        ViewBag.LatestYear = latestYear;
        ViewBag.AdminName = HttpContext.Session.GetString("AdminUser");

        return View("admin_dashboard");
    }

    public IActionResult Logout()
    {
        HttpContext.Session. Clear();
        return RedirectToAction("Login");
    }

    private bool VerifyPassword(string password, string storedHash)
    {
        return HashPassword(password) == storedHash;
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

    public async Task<IActionResult> RegisterStaff([FromBody] Staff staff)
    {
        if (staff == null)
        {
            return BadRequest("Invalid data.");
        }

        // Generate a random salt for the password
        var salt = new byte[16];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        // Hash the password using the generated salt
        var hashedPassword = Convert.ToBase64String(KeyDerivation.Pbkdf2(
            password: staff.PasswordHash,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: 10000,
            numBytesRequested: 256 / 8));

        // Store the hashed password and the salt in the database
        staff.PasswordHash = hashedPassword;
        staff.PasswordSalt = Convert.ToBase64String(salt);

        // Add the staff to the database
        _context.Staffs.Add(staff);
        await _context.SaveChangesAsync();

        return Json(new { message = "Staff successfully registered." });
    }

     public IActionResult Services()
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
        {
            return RedirectToAction("Login");
        }

        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        ViewBag.AntiForgeryToken = tokens.RequestToken;
        return View("admin_services");
    }

    public IActionResult SecurityVisitors()
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
        {
            return RedirectToAction("Login");
        }

        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        ViewBag.AntiForgeryToken = tokens.RequestToken;
        ViewBag.AdminName = HttpContext.Session.GetString("AdminUser");
        return View("admin_security_visitors");
    }

    [HttpGet]
    public async Task<IActionResult> GetServiceCategories()
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();

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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddServiceCategory([FromBody] ServiceCategory category)
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();

        try
        {
            _context.ServiceCategories.Add(category);
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Category added successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding service category");
            return StatusCode(500, new { success = false, message = "Error adding category" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetServiceEmployees()
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();

        try
        {
            var employees = await _context.Staffs
                .Where(s => s.Role == "ServiceEmployee" || s.Role == "Staff")
                .Select(s => new
                {
                    s.Id,
                    s.Username,
                    Name = s.FullName,
                    s.Email,
                    Phone = s.ContactNumber,
                    s.Status,
                    s.Specialization,
                    s.Department,
                    s.Position
                })
                .ToListAsync();

            return Json(employees);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching service employees");
            return StatusCode(500, new { message = "Error fetching service employees" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetServiceLogs()
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();

        try
        {
            var logs = await _context.ServiceRequests
                .Include(sr => sr.Homeowner)
                .Include(sr => sr.Staff)
                .OrderByDescending(sr => sr.DateSubmitted)
                .Select(sr => new
                {
                    sr.Id,
                    RequestId = $"SR-{sr.Id:D3}",
                    HomeownerName = sr.Homeowner != null ? $"{sr.Homeowner.FirstName} {sr.Homeowner.LastName}" : "N/A",
                    sr.ServiceType,
                    sr.Priority,
                    sr.Status,
                    DateSubmitted = sr.DateSubmitted,
                    sr.Description,
                    sr.DateAccepted,
                    sr.DateStarted,
                    sr.DateCompleted,
                    CompletionTime = sr.DateCompleted.HasValue && sr.DateAccepted.HasValue 
                        ? (sr.DateCompleted.Value - sr.DateAccepted.Value).TotalDays.ToString("F1") + " days"
                        : null,
                    sr.StaffNotes,
                    StaffName = sr.Staff != null ? sr.Staff.FullName : null
                })
                .ToListAsync();

            return Json(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching service logs");
            return StatusCode(500, new { message = "Error fetching service logs" });
        }
    }

    [HttpPut]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateServiceCategory([FromBody] ServiceCategory category)
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();

        try
        {
            var existingCategory = await _context.ServiceCategories.FindAsync(category.Id);
            if (existingCategory == null)
                return NotFound(new { success = false, message = "Category not found" });

            existingCategory.Name = category.Name;
            existingCategory.Description = category.Description;
            existingCategory.Icon = category.Icon;
            existingCategory.DateModified = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Category updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating service category");
            return StatusCode(500, new { success = false, message = "Error updating category" });
        }
    }

    [HttpDelete]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteServiceCategory(int id)
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();

        try
        {
            var category = await _context.ServiceCategories.FindAsync(id);
            if (category == null)
                return NotFound(new { success = false, message = "Category not found" });

            _context.ServiceCategories.Remove(category);
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Category deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting service category");
            return StatusCode(500, new { success = false, message = "Error deleting category" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddServiceEmployee([FromBody] ServiceEmployeeDto employeeDto)
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();

        try
        {
            var staff = new Staff
            {
                FullName = employeeDto.Name,
                Email = employeeDto.Email,
                ContactNumber = employeeDto.Phone,
                Specialization = employeeDto.Specialization,
                Role = "ServiceEmployee",
                Status = "active",
                Username = employeeDto.Email, // Using email as username
                PasswordHash = HashPassword("password123") // Default password
            };

            _context.Staffs.Add(staff);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Employee added successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding service employee");
            return StatusCode(500, new { success = false, message = "Error adding employee" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateEmployeeStatus([FromBody] UpdateEmployeeStatusDto model)
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();

        try
        {
            var employee = await _context.Staffs.FindAsync(model.EmployeeId);
            if (employee == null)
                return NotFound(new { success = false, message = "Employee not found" });

            employee.Status = model.Status;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Employee status updated to {model.Status}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating employee status");
            return StatusCode(500, new { success = false, message = "Error updating employee status" });
        }
    }

    [HttpPut]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateServiceEmployee([FromBody] UpdateServiceEmployeeDto model)
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();

        try
        {
            var employee = await _context.Staffs.FindAsync(model.Id);
            if (employee == null)
                return NotFound(new { success = false, message = "Employee not found" });

            employee.FullName = model.Name;
            employee.Email = model.Email;
            employee.ContactNumber = model.Phone;
            employee.Specialization = model.Specialization;
            employee.Username = model.Email; // Update username to match new email

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Employee updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating service employee");
            return StatusCode(500, new { success = false, message = "Error updating employee" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllVisitorPasses()
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();

        try
        {
            _logger.LogInformation("Fetching visitor passes from database");
            
            var passes = await _context.VisitorPasses
                .Include(vp => vp.Homeowner)
                .OrderByDescending(vp => vp.VisitDate)
                .Select(vp => new
                {
                    vp.Id,
                    vp.VisitorName,
                    vp.VisitDate,
                    vp.Purpose,
                    vp.Status,
                    vp.EntryTime,
                    vp.ExitTime,
                    HomeownerName = vp.Homeowner != null ? $"{vp.Homeowner.FirstName} {vp.Homeowner.LastName}" : "N/A",
                    RequestDate = vp.VisitDate
                })
                .ToListAsync();

            _logger.LogInformation($"Retrieved {passes.Count} visitor passes");
            return Json(passes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching visitor passes");
            return StatusCode(500, new { message = "Error fetching visitor passes", error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllVehicleRegistrations()
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();

        try
        {
            _logger.LogInformation("Fetching vehicle registrations from database");
            
            var registrations = await _context.VehicleRegistrations
                .Include(vr => vr.Homeowner)
                .OrderByDescending(vr => vr.RegistrationDate)
                .Select(vr => new
                {
                    vr.Id,
                    vr.VehicleMake,
                    vr.VehicleModel,
                    vr.PlateNumber,
                    vr.Status,
                    vr.RegistrationDate,
                    HomeownerName = vr.Homeowner != null ? $"{vr.Homeowner.FirstName} {vr.Homeowner.LastName}" : "N/A"
                })
                .ToListAsync();

            _logger.LogInformation($"Retrieved {registrations.Count} vehicle registrations");
            return Json(registrations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching vehicle registrations");
            return StatusCode(500, new { message = "Error fetching vehicle registrations", error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportVisitorPasses()
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();

        try
        {
            var passes = await _context.VisitorPasses
                .Include(vp => vp.Homeowner)
                .OrderByDescending(vp => vp.VisitDate)
                .Select(vp => new
                {
                    vp.VisitorName,
                    VisitDate = vp.VisitDate.ToString("MM/dd/yyyy"),
                    vp.Purpose,
                    vp.Status,
                    EntryTime = vp.EntryTime.HasValue ? vp.EntryTime.Value.ToString("HH:mm:ss") : "N/A",
                    ExitTime = vp.ExitTime.HasValue ? vp.ExitTime.Value.ToString("HH:mm:ss") : "N/A",
                    HomeownerName = vp.Homeowner != null ? $"{vp.Homeowner.FirstName} {vp.Homeowner.LastName}" : "N/A",
                    RequestDate = vp.VisitDate.ToString("MM/dd/yyyy")
                })
                .ToListAsync();

            return Json(passes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting visitor passes");
            return StatusCode(500, new { message = "Error exporting visitor passes", error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportVehicleRegistrations()
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();

        try
        {
            var registrations = await _context.VehicleRegistrations
                .Include(vr => vr.Homeowner)
                .OrderByDescending(vr => vr.RegistrationDate)
                .Select(vr => new
                {
                    vr.VehicleMake,
                    vr.VehicleModel,
                    vr.PlateNumber,
                    vr.Status,
                    RegistrationDate = vr.RegistrationDate.ToString("MM/dd/yyyy"),
                    HomeownerName = vr.Homeowner != null ? $"{vr.Homeowner.FirstName} {vr.Homeowner.LastName}" : "N/A"
                })
                .ToListAsync();

            return Json(registrations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting vehicle registrations");
            return StatusCode(500, new { message = "Error exporting vehicle registrations", error = ex.Message });
        }
    }

    public class ServiceEmployeeDto
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Specialization { get; set; } = string.Empty;
    }

    public class UpdateEmployeeStatusDto
    {
        public int EmployeeId { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class UpdateServiceEmployeeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Specialization { get; set; } = string.Empty;
    }

    public IActionResult AdminFacilityReservation()
    {
        var facilities = _context.Facilities.ToList(); // Fetch facilities from the database
        return View(facilities); // Pass the facilities list to the view
    }

    [HttpPost]
    public async Task<IActionResult> AddFacility(Facility facility, IFormFile ImagePath)
    {
        if (ImagePath != null)
        {
            var filePath = Path.Combine("wwwroot/images", ImagePath.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await ImagePath.CopyToAsync(stream);
            }
            facility.ImagePath = $"/images/{ImagePath.FileName}";
        }

        _context.Facilities.Add(facility);
        await _context.SaveChangesAsync();
        return RedirectToAction("AdminFacilityReservation");
    }

    [HttpPost]
    public async Task<IActionResult> EditFacility(Facility facility, IFormFile? ImagePath)
    {
        var existingFacility = await _context.Facilities.FindAsync(facility.Id);
        if (existingFacility == null)
        {
            return NotFound();
        }

        existingFacility.Name = facility.Name;
        existingFacility.Description = facility.Description;
        existingFacility.Capacity = facility.Capacity;
        existingFacility.Status = facility.Status;

        if (ImagePath != null)
        {
            var filePath = Path.Combine("wwwroot/images", ImagePath.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await ImagePath.CopyToAsync(stream);
            }
            existingFacility.ImagePath = $"/images/{ImagePath.FileName}";
        }

        _context.Facilities.Update(existingFacility);
        await _context.SaveChangesAsync();

        return RedirectToAction("AdminFacilityReservation");
    }

    // API: Get reservations for a facility (Admin)
    [HttpGet]
    public IActionResult GetFacilityReservations(int facilityId)
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();
        var reservations = _context.FacilityReservations
            .Include(r => r.Homeowner)
            .Where(r => r.FacilityId == facilityId)
            .OrderByDescending(r => r.DateSubmitted)
            .ToList();
        return Json(reservations);
    }

    // API: Update reservation status and note (Admin)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateFacilityReservationStatus(int reservationId, string status, string? note)
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Unauthorized();
        var reservation = await _context.FacilityReservations.FindAsync(reservationId);
        if (reservation == null)
            return NotFound();
        reservation.Status = status;
        reservation.Note = note;
        reservation.LastUpdatedBy = "admin";
        _context.FacilityReservations.Update(reservation);
        await _context.SaveChangesAsync();
        return Json(new { success = true, message = "Reservation updated." });
    }

    public IActionResult AdminBilling()
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
        {
            return RedirectToAction("Login");
        }
        var billings = _context.Billings.ToList();
        return View("AdminBilling", billings);
    }

    [HttpPost]
    public async Task<IActionResult> AddBilling([FromForm] string Type, [FromForm] decimal Amount, 
        [FromForm] string Status, [FromForm] string? Note, [FromForm] int HomeownerId, 
        [FromForm] DateTime Date, IFormFile? ReceiptFile)
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Json(new { success = false, message = "Unauthorized" });
        try
        {
            var billing = new Billing
            {
                Type = Type,
                Amount = Amount,
                Status = Status,
                Note = Note,
                HomeownerId = HomeownerId,
                Date = Date,
                LastUpdatedBy = "admin"
            };

            if (ReceiptFile != null)
            {
                // Create directory if it doesn't exist
                var uploadDir = Path.Combine("wwwroot", "uploads", "receipts");
                Directory.CreateDirectory(uploadDir);

                // Generate unique filename
                var fileName = $"{Guid.NewGuid()}_{ReceiptFile.FileName}";
                var filePath = Path.Combine(uploadDir, fileName);

                // Save file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await ReceiptFile.CopyToAsync(stream);
                }

                // Save relative path to database
                billing.ReceiptPath = $"/uploads/receipts/{fileName}";
            }

            _context.Billings.Add(billing);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error saving billing: " + ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> MarkAsPaid(int id, string? note)
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Json(new { success = false, message = "Unauthorized" });
    
        var billing = await _context.Billings.FindAsync(id);
        if (billing == null)
            return NotFound();
    
        billing.Status = "Paid";
        billing.Note = note;
        billing.LastUpdatedBy = "admin";
    
        _context.Billings.Update(billing);
        await _context.SaveChangesAsync();
    
        // Redirect to AdminBilling after marking as paid
        return RedirectToAction("AdminBilling");
    }

    [HttpGet]
    public async Task<IActionResult> DownloadReceipt(int id)
    {
        var billing = await _context.Billings.FindAsync(id);
        if (billing == null || string.IsNullOrEmpty(billing.ReceiptPath))
            return NotFound();
    
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", billing.ReceiptPath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
        if (!System.IO.File.Exists(filePath))
            return NotFound();
    
        var fileName = Path.GetFileName(filePath);
        var contentType = "application/octet-stream";
        return PhysicalFile(filePath, contentType, fileName);
    }
    
    [HttpPost]
    public async Task<IActionResult> EditBilling([FromForm] int Id, [FromForm] string Type, [FromForm] decimal Amount,
        [FromForm] string Status, [FromForm] string? Note, [FromForm] int HomeownerId,
        [FromForm] DateTime Date, [FromForm] string? LastUpdatedBy, IFormFile? ReceiptFile)
    {
        var billing = await _context.Billings.FindAsync(Id);
        if (billing == null)
            return Json(new { success = false, message = "Billing not found." });
    
        billing.Type = Type;
        billing.Amount = Amount;
        billing.Status = Status;
        billing.Note = Note;
        billing.HomeownerId = HomeownerId;
        billing.Date = Date;
        billing.LastUpdatedBy = LastUpdatedBy ?? "admin";
    
        if (ReceiptFile != null)
        {
            // Delete old receipt if exists
            if (!string.IsNullOrEmpty(billing.ReceiptPath))
            {
                var oldPath = Path.Combine("wwwroot", billing.ReceiptPath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);
            }
    
            var uploadDir = Path.Combine("wwwroot", "uploads", "receipts");
            Directory.CreateDirectory(uploadDir);
            var fileName = $"{Guid.NewGuid()}_{ReceiptFile.FileName}";
            var filePath = Path.Combine(uploadDir, fileName);
    
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await ReceiptFile.CopyToAsync(stream);
            }
            billing.ReceiptPath = $"/uploads/receipts/{fileName}";
        }
    
        _context.Billings.Update(billing);
        await _context.SaveChangesAsync();
        return Json(new { success = true });
    }
    
    [HttpPost]
    public async Task<IActionResult> EditBilling([FromForm] int Id, [FromForm] string Type, [FromForm] DateTime Date,
        [FromForm] string Status, [FromForm] string? Note, [FromForm] int HomeownerId, IFormFile? ReceiptFile)
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
            return Json(new { success = false, message = "Unauthorized" });
    
        var billing = await _context.Billings.FindAsync(Id);
        if (billing == null)
            return Json(new { success = false, message = "Billing not found." });
    
        billing.Type = Type;
        billing.Date = Date;
        billing.Status = Status;
        billing.Note = Note;
        billing.HomeownerId = HomeownerId;
    
        // Set LastUpdatedBy based on session
        if (HttpContext.Session.GetString("AdminUser") != null)
            billing.LastUpdatedBy = "admin";
        else if (HttpContext.Session.GetString("StaffUser") != null)
            billing.LastUpdatedBy = HttpContext.Session.GetString("StaffUser");
        else
            billing.LastUpdatedBy = "unknown";
    
        if (ReceiptFile != null)
        {
            var uploadDir = Path.Combine("wwwroot", "uploads", "receipts");
            Directory.CreateDirectory(uploadDir);
            var fileName = $"{Guid.NewGuid()}_{ReceiptFile.FileName}";
            var filePath = Path.Combine(uploadDir, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await ReceiptFile.CopyToAsync(stream);
            }
            billing.ReceiptPath = $"/uploads/receipts/{fileName}";
        }
    
        _context.Billings.Update(billing);
        await _context.SaveChangesAsync();
        return Json(new { success = true });
    }
    
    [HttpPost]
    public async Task<IActionResult> DeleteBilling(int id)
    {
        var billing = await _context.Billings.FindAsync(id);
        if (billing == null)
            return Json(new { success = false, message = "Billing not found." });
    
        // Delete receipt file if exists
        if (!string.IsNullOrEmpty(billing.ReceiptPath))
        {
            var filePath = Path.Combine("wwwroot", billing.ReceiptPath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);
        }
    
        _context.Billings.Remove(billing);
        await _context.SaveChangesAsync();
        return Json(new { success = true });
    }

    public IActionResult Profile()
    {
        if (HttpContext.Session.GetString("AdminUser") == null)
        {
            return RedirectToAction("Login");
        }
        ViewBag.AdminName = HttpContext.Session.GetString("AdminUser");
        ViewBag.Homeowners = _context.Homeowners.ToList();
        ViewBag.Employees = _context.Staffs.ToList();
        return View("admin_profile");
    }
}
