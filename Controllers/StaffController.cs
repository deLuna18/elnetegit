using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using SubdivisionManagement.Model;
using System.Linq;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using System.IO;
using System;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace SubdivisionManagement.Controllers
{
    public class StaffController : Controller
    {
        private readonly HomeContext _context;
        private readonly IAntiforgery _antiforgery;
        private readonly ILogger<StaffController> _logger;

        public StaffController(HomeContext context, IAntiforgery antiforgery, ILogger<StaffController> logger)
        {
            _context = context;
            _antiforgery = antiforgery;
            _logger = logger;
        }

        // Helper to check if staff is logged in
        private bool IsStaffLoggedIn(out string? username)
        {
            username = HttpContext.Session.GetString("StaffUser");
            return !string.IsNullOrEmpty(username);
        }

        // Helper to get logged in staff user
        private Staff? GetLoggedInStaffUser(string? username)
        {
            if (string.IsNullOrEmpty(username))
                return null;
            return _context.Staffs.FirstOrDefault(s => s.Username == username);
        }

        // GET: Staff/Login (Displays the login page)
        public IActionResult Login()
        {
            var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
            ViewBag.AntiForgeryToken = tokens.RequestToken;
            return View();
        }

        // POST: Staff/Login (Handles the login logic)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Login(string username, string password)
        {
            var staff = _context.Staffs.FirstOrDefault(s => s.Username == username);

            if (staff == null || !VerifyPassword(password, staff.PasswordHash, staff.PasswordSalt))
            {
                ViewBag.Error = "Invalid username or password";
                return View();
            }

            HttpContext.Session.SetString("StaffUser", username);
            return RedirectToAction("Dashboard");
        }

        public IActionResult Dashboard()
        {
            if (!IsStaffLoggedIn(out var username))
            {
                return RedirectToAction("Login");
            }

            var staff = GetLoggedInStaffUser(username);
            if (staff == null)
            {
                return RedirectToAction("Login");
            }

            // Get all homeowners (not just pending)
            var homeowners = _context.Homeowners.ToList();
            ViewBag.Homeowners = homeowners;

            // Get announcements and add to ViewBag
            var announcements = _context.Announcements
                .Include(a => a.Staff)
                .OrderByDescending(a => a.DateCreated)
                .ToList();
            ViewBag.Announcements = announcements;

            return View("staffdashboard", staff);
        }

        [HttpGet]
        public IActionResult Staff_Services()
        {
            if (!IsStaffLoggedIn(out _))
            {
                return RedirectToAction("Login");
            }
            var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
            ViewBag.AntiForgeryToken = tokens.RequestToken;
            return View();
        }

        // GET: /Staff/GetServiceRequests
        [HttpGet]
        public async Task<IActionResult> GetServiceRequests()
        {
            if (!IsStaffLoggedIn(out _)) return Unauthorized();

            try 
            {
                var rawRequests = await _context.ServiceRequests
                    .Include(r => r.Homeowner) 
                    .OrderByDescending(r => r.DateSubmitted)
                    .ToListAsync(); 

                var projectedRequests = rawRequests.Select(r => new 
                {
                    r.Id,
                    RequestId = $"SR-{r.Id:D3}",
                    HomeownerName = (r.Homeowner != null) ? $"{r.Homeowner.FirstName} {r.Homeowner.LastName}" : "N/A",
                    r.HomeownerId,
                    r.ServiceType,
                    r.Priority,
                    r.Status,
                    DateSubmitted = r.DateSubmitted.ToString("o"),
                    r.Description,
                    DateCompleted = r.DateCompleted.HasValue ? r.DateCompleted.Value.ToString("o") : null,
                    CompletionTime = CalculateCompletionTime(r.DateAccepted, r.DateCompleted),
                    r.StaffNotes,
                    r.StaffId
                }).ToList(); 

                return Json(projectedRequests);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching service requests in GetServiceRequests action.");
                return StatusCode(500, new { message = "An internal error occurred while retrieving service requests." }); 
            }
        }

        // POST: /Staff/UpdateServiceRequestStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateServiceRequestStatus([FromBody] UpdateServiceRequestDto updateDto)
        {
            if (!IsStaffLoggedIn(out var username)) return Unauthorized();
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var staff = GetLoggedInStaffUser(username);
            if (staff == null) return Forbid(); 

            var request = await _context.ServiceRequests.FindAsync(updateDto.RequestId);
            if (request == null)
            {
                return NotFound(new { success = false, message = "Service request not found." });
            }

            if (!IsValidStatusTransition(request.Status, updateDto.NewStatus))
            {
                return BadRequest(new { success = false, message = $"Invalid status transition from {request.Status} to {updateDto.NewStatus}." });
            }

            request.Status = updateDto.NewStatus;
            request.StaffId = staff.Id; 
            request.StaffNotes = updateDto.StaffNotes ?? request.StaffNotes; 

            switch (updateDto.NewStatus.ToLower())
            {
                case "accepted":
                    request.DateAccepted = DateTime.UtcNow;
                    break;
                case "in-progress":
                    if (request.DateAccepted == null) request.DateAccepted = DateTime.UtcNow; 
                    request.DateStarted = DateTime.UtcNow;
                    break;
                case "completed":
                    if (request.DateAccepted == null) request.DateAccepted = DateTime.UtcNow;
                    if (request.DateStarted == null) request.DateStarted = DateTime.UtcNow; 
                    request.DateCompleted = DateTime.UtcNow;
                    break;
                case "rejected":
                    request.DateAccepted = null;
                    request.DateStarted = null;
                    request.DateCompleted = null;
                    break;
            }

            try
            {
                _context.ServiceRequests.Update(request);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Request status updated to {updateDto.NewStatus}." });
            }
            catch (DbUpdateConcurrencyException)
            {
                 return Conflict(new { success = false, message = "The request was modified by another user. Please refresh and try again." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating service request status for ID {RequestId}", updateDto.RequestId);
                return StatusCode(500, new { success = false, message = "An internal error occurred while updating the status." });
            }
        }

        // POST: /Staff/UpdateStaffNotes
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStaffNotes([FromBody] UpdateStaffNotesDto notesDto)
        {
             if (!IsStaffLoggedIn(out var username)) return Unauthorized();
             if (!ModelState.IsValid) return BadRequest(ModelState);

             var staff = GetLoggedInStaffUser(username);
             if (staff == null) return Forbid();

             var request = await _context.ServiceRequests.FindAsync(notesDto.RequestId);
             if (request == null)
             {
                 return NotFound(new { success = false, message = "Service request not found." });
             }

             
             request.StaffNotes = notesDto.StaffNotes;
             request.StaffId = staff.Id; 
             try
             {
                 _context.ServiceRequests.Update(request);
                 await _context.SaveChangesAsync();
                 return Json(new { success = true, message = "Staff notes updated successfully." });
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Error updating staff notes for ID {RequestId}", notesDto.RequestId);
                 return StatusCode(500, new { success = false, message = "An internal error occurred while updating notes." });
             }
        }

        private bool IsValidStatusTransition(string currentStatus, string newStatus)
        {
            currentStatus = currentStatus.ToLower();
            newStatus = newStatus.ToLower();

            if (currentStatus == newStatus) return true; 
            if (currentStatus == "completed" || currentStatus == "rejected") return false; 

            switch (currentStatus)
            {
                case "pending":
                    return newStatus == "accepted" || newStatus == "rejected";
                case "accepted":
                    return newStatus == "in-progress" || newStatus == "completed" || newStatus == "rejected"; 
                case "in-progress":
                    return newStatus == "completed" || newStatus == "rejected"; 
                default:
                    return false; 
            }
        }

        public class UpdateServiceRequestDto
        {
            [Required] public int RequestId { get; set; }
            [Required] public string NewStatus { get; set; } = string.Empty;
            public string? StaffNotes { get; set; }
        }

        public class UpdateStaffNotesDto
        {
            [Required] public int RequestId { get; set; }
            [Required] public string StaffNotes { get; set; } = string.Empty;
        }

        private string? CalculateCompletionTime(DateTime? start, DateTime? end)
        {
             if (!start.HasValue || !end.HasValue || end.Value < start.Value) 
             {
                return null; 
             }
             return CalculateBusinessTime(start.Value, end.Value); 
        }

        private string CalculateBusinessTime(DateTime start, DateTime end)
        {
            TimeSpan duration = end - start;
            if (duration.TotalDays >= 1)
            {
                return $"{duration.TotalDays:F1} days";
            }
            else if (duration.TotalHours >= 1)
            {
                return $"{duration.TotalHours:F1} hours";
            }
            else
            {
                return $"{duration.TotalMinutes:F0} minutes";
            }
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        // Password verification logic
        private bool VerifyPassword(string password, string storedHash, string? storedSalt)
        {
            // Check if the storedSalt is null before proceeding
            if (storedSalt == null)
            {
                // If the salt is null, return false or handle accordingly
                return false;
            }

            // Convert the stored salt from base64 string to byte array
            byte[] salt = Convert.FromBase64String(storedSalt);

            // Log the salt being used (optional, for debugging)
            Console.WriteLine($"Salt: {Convert.ToBase64String(salt)}");

            // Compute the hash from the input password using the salt
            var computedHash = Convert.ToBase64String(KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: 10000,
                numBytesRequested: 256 / 8));

            // Log the computed hash and stored hash
            Console.WriteLine($"Computed Hash: {computedHash}");
            Console.WriteLine($"Stored Hash: {storedHash}");

            // Compare the computed hash with the stored hash
            return computedHash == storedHash;
        }

        // GET: Staff/Register (Displays the registration page)
        public IActionResult Register()
        {
            return View();
        }

        // POST: Staff/Register (Handles the registration logic)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(Staff staff)
        {
            if (staff == null)
            {
                return BadRequest("Invalid data.");
            }

            // Generate a random salt for password
            var salt = new byte[16];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            // Hash the password with the generated salt
            var hashedPassword = Convert.ToBase64String(KeyDerivation.Pbkdf2(
                password: staff.PasswordHash,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: 10000,
                numBytesRequested: 256 / 8));

            // Store the hashed password and the salt in the database
            staff.PasswordHash = hashedPassword;
            staff.PasswordSalt = Convert.ToBase64String(salt); // Save the salt as well

            // Add the staff to the database
            _context.Staffs.Add(staff);
            await _context.SaveChangesAsync();

            return RedirectToAction("Login");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateHomeownerStatus([FromBody] UpdateStatusModel model)
        {
            try
            {
                var homeowner = _context.Homeowners.Find(model.HomeownerId);
                if (homeowner == null)
                {
                    return Json(new { success = false, message = "Homeowner not found" });
                }

                // Get the current staff user
                if (!IsStaffLoggedIn(out var username)) return Unauthorized();
                var staff = GetLoggedInStaffUser(username);
                
                if (staff == null)
                {
                    return Json(new { success = false, message = "Staff not found" });
                }

                try
                {
                    homeowner.Status = model.NewStatus;
                    homeowner.StaffId = staff.Id;  // Update to StaffId
                    _context.SaveChanges();

                    return Json(new { success = true, message = $"Status updated to {model.NewStatus}" });
                }
                catch (Exception ex)
                {
                    // Log the inner exception details
                    Console.WriteLine($"Inner Exception: {ex.InnerException?.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddAnnouncement(IFormFile image, string type, string description)
        {
            try
            {
                if (!IsStaffLoggedIn(out var username)) return Unauthorized();
                var staff = GetLoggedInStaffUser(username);
                
                if (staff == null)
                {
                    return Json(new { success = false, message = "Staff not found" });
                }

                var announcement = new Announcement
                {
                    Type = type,
                    Description = description,
                    StaffId = staff.Id
                };

                if (image != null && image.Length > 0)
                {
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "announcements");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var uniqueFileName = Guid.NewGuid().ToString() + "_" + image.FileName;
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await image.CopyToAsync(fileStream);
                    }

                    announcement.ImagePath = "/uploads/announcements/" + uniqueFileName;
                }

                _context.Announcements.Add(announcement);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Announcement added successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAnnouncement(int id, string type, string description, IFormFile? image)
        {
            try
            {
                if (!IsStaffLoggedIn(out _)) return Unauthorized(); // Check login

                var announcement = _context.Announcements.FirstOrDefault(a => a.Id == id);
                if (announcement == null)
                {
                    return Json(new { success = false, message = "Announcement not found." });
                }

                // Update announcement details
                announcement.Type = type;
                announcement.Description = description;

                // Handle image upload if provided
                if (image != null && image.Length > 0)
                {
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "announcements");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var uniqueFileName = Guid.NewGuid().ToString() + "_" + image.FileName;
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await image.CopyToAsync(fileStream);
                    }

                    announcement.ImagePath = "/uploads/announcements/" + uniqueFileName;
                }

                _context.Announcements.Update(announcement);
                await _context.SaveChangesAsync();

                // Return success response
                return Json(new { success = true, message = "Announcement updated successfully." });
            }
            catch (Exception ex)
            {
                // Log the exception and return an error response
                Console.WriteLine($"Error: {ex.Message}");
                return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteAnnouncement(int id)
        {
            try
            {
                if (!IsStaffLoggedIn(out _)) return Unauthorized(); // Check login

                var announcement = _context.Announcements.FirstOrDefault(a => a.Id == id);
                if (announcement == null)
                {
                    return Json(new { success = false, message = "Announcement not found." });
                }

                _context.Announcements.Remove(announcement);
                _context.SaveChanges();

                return Json(new { success = true, message = "Announcement deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
            }
        }

        public IActionResult Staff_Contact_And_Support()
        {
            if (!IsStaffLoggedIn(out _))
            {
                return RedirectToAction("Login");
            }

            try
            {
                var contactRequests = _context.ContactRequests
                    .Include(c => c.Homeowner)
                    .OrderByDescending(c => c.DateSubmitted)
                    .ToList();

                // Return the view with the model
                return View(contactRequests);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading contact requests: {ex.Message}");
                return View(new List<ContactRequest>());
            }
        }
        

        public IActionResult Announcements()
        {
            if (!IsStaffLoggedIn(out _)) return RedirectToAction("Login"); // Check login

            var announcements = _context.Announcements
                .Include(a => a.Staff)
                .OrderByDescending(a => a.DateCreated)
                .ToList();
            
            return View("staff_announcement", announcements);
        }

        public IActionResult Staff_Security_Visitors()
        {
            if (!IsStaffLoggedIn(out _)) return RedirectToAction("Login"); // Check login

            return View("staff_security_visitors");
        }

        public IActionResult Staff_Community_Forum()
        {
            if (!IsStaffLoggedIn(out _)) return RedirectToAction("Login"); // Check login

            return View("staff_community_forum");
        }

        public class UpdateStatusModel
        {
            public int HomeownerId { get; set; }
            public string NewStatus { get; set; } = string.Empty;
        }

        // STAFF FACILITY RESERVATION MANAGEMENT
        [HttpGet]
        public IActionResult StaffFacilityReservation()
        {
            if (!IsStaffLoggedIn(out _))
            {
                return RedirectToAction("Login");
            }
            var facilities = _context.Facilities.ToList();
            return View("StaffFacilityReservation", facilities);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddFacility(Facility facility, IFormFile ImagePath)
        {
            if (!IsStaffLoggedIn(out _))
            {
                return RedirectToAction("Login");
            }
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
            return RedirectToAction("StaffFacilityReservation");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditFacility(Facility facility, IFormFile? ImagePath)
        {
            if (!IsStaffLoggedIn(out _))
            {
                return RedirectToAction("Login");
            }
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
            return RedirectToAction("StaffFacilityReservation");
        }

        // API: Get reservations for a facility
        [HttpGet]
        public IActionResult GetFacilityReservations(int facilityId)
        {
            if (!IsStaffLoggedIn(out _))
                return Unauthorized();
            var reservations = _context.FacilityReservations
                .Include(r => r.Homeowner)
                .Where(r => r.FacilityId == facilityId)
                .OrderByDescending(r => r.DateSubmitted)
                .ToList();
            return Json(reservations);
        }

        // API: Update reservation status and note
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateFacilityReservationStatus(int reservationId, string status, string? note)
        {
            if (!IsStaffLoggedIn(out _))
                return Unauthorized();
            var reservation = await _context.FacilityReservations.FindAsync(reservationId);
            if (reservation == null)
                return NotFound();
            reservation.Status = status;
            reservation.Note = note;
            // Set LastUpdatedBy to staff's Id as string
            var staff = GetLoggedInStaffUser(HttpContext.Session.GetString("StaffUser"));
            if (staff != null) reservation.LastUpdatedBy = staff.Id.ToString();
            _context.FacilityReservations.Update(reservation);
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Reservation updated." });
        }

        public IActionResult StaffBilling()
        {
            if (!IsStaffLoggedIn(out _))
            {
                return RedirectToAction("Login");
            }

            var billings = _context.Billings
                .OrderByDescending(b => b.Date)  // Changed from DateCreated to Date
                .ToList();
            
            return View(billings);
        }

 [HttpPost]
    public async Task<IActionResult> AddBilling([FromForm] string Type, [FromForm] decimal Amount, 
        [FromForm] string Status, [FromForm] string? Note, [FromForm] int HomeownerId, 
        [FromForm] DateTime Date, IFormFile? ReceiptFile)
    {
        if (!IsStaffLoggedIn(out var username))
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
                LastUpdatedBy = username
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
        if (!IsStaffLoggedIn(out var username))
            return Json(new { success = false, message = "Unauthorized" });
    
        var billing = await _context.Billings.FindAsync(id);
        if (billing == null)
            return NotFound();
    
        billing.Status = "Paid";
        billing.Note = note;
        billing.LastUpdatedBy = username;
    
        _context.Billings.Update(billing);
        await _context.SaveChangesAsync();
    
        // Redirect to AdminBilling after marking as paid
        return RedirectToAction("StaffBilling");
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

    [HttpGet]
    public async Task<IActionResult> GetServiceEmployees()
    {
        if (!IsStaffLoggedIn(out _))
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
    public async Task<IActionResult> GetServiceCategories()
    {
        if (!IsStaffLoggedIn(out _))
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

    [HttpGet]
    public async Task<IActionResult> GetPendingVisitorPasses()
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var passes = await _context.VisitorPasses
                .Include(vp => vp.Homeowner)
                .Where(vp => vp.Status == "Pending")
                .OrderByDescending(vp => vp.RequestDate)
                .Select(vp => new
                {
                    vp.Id,
                    vp.VisitorName,
                    VisitDate = vp.VisitDate.ToString("yyyy-MM-dd"),
                    vp.Purpose,
                    HomeownerName = vp.Homeowner != null ? $"{vp.Homeowner.FirstName} {vp.Homeowner.LastName}" : "N/A"
                })
                .ToListAsync();

            return Json(passes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching pending visitor passes");
            return StatusCode(500, new { message = "Error fetching pending visitor passes" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetApprovedVisitorPasses()
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var passes = await _context.VisitorPasses
                .Include(vp => vp.Homeowner)
                .Where(vp => vp.Status == "Approved")
                .OrderByDescending(vp => vp.RequestDate)
                .Select(vp => new
                {
                    vp.Id,
                    vp.VisitorName,
                    VisitDate = vp.VisitDate.ToString("yyyy-MM-dd"),
                    vp.Purpose,
                    HomeownerName = vp.Homeowner != null ? $"{vp.Homeowner.FirstName} {vp.Homeowner.LastName}" : "N/A"
                })
                .ToListAsync();

            return Json(passes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching approved visitor passes");
            return StatusCode(500, new { message = "Error fetching approved visitor passes" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetPendingVehicleRegistrations()
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var registrations = await _context.VehicleRegistrations
                .Include(vr => vr.Homeowner)
                .Where(vr => vr.Status == "Pending")
                .OrderByDescending(vr => vr.RegistrationDate)
                .Select(vr => new
                {
                    vr.Id,
                    vr.VehicleMake,
                    vr.VehicleModel,
                    vr.PlateNumber,
                    HomeownerName = vr.Homeowner != null ? $"{vr.Homeowner.FirstName} {vr.Homeowner.LastName}" : "N/A"
                })
                .ToListAsync();

            return Json(registrations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching pending vehicle registrations");
            return StatusCode(500, new { message = "Error fetching pending vehicle registrations" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetApprovedVehicleRegistrations()
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var registrations = await _context.VehicleRegistrations
                .Include(vr => vr.Homeowner)
                .Where(vr => vr.Status == "Approved")
                .OrderByDescending(vr => vr.RegistrationDate)
                .Select(vr => new
                {
                    vr.Id,
                    vr.VehicleMake,
                    vr.VehicleModel,
                    vr.PlateNumber,
                    HomeownerName = vr.Homeowner != null ? $"{vr.Homeowner.FirstName} {vr.Homeowner.LastName}" : "N/A"
                })
                .ToListAsync();

            return Json(registrations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching approved vehicle registrations");
            return StatusCode(500, new { message = "Error fetching approved vehicle registrations" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetActiveVisitors()
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var visitors = await _context.VisitorPasses
                .Include(vp => vp.Homeowner)
                .Where(vp => vp.Status == "Approved" && vp.EntryTime.HasValue && !vp.ExitTime.HasValue)
                .OrderByDescending(vp => vp.EntryTime)
                .Select(vp => new
                {
                    vp.Id,
                    vp.VisitorName,
                    EntryTime = vp.EntryTime.Value.ToString("yyyy-MM-dd HH:mm:ss"),
                    vp.Purpose,
                    HomeownerName = vp.Homeowner != null ? $"{vp.Homeowner.FirstName} {vp.Homeowner.LastName}" : "N/A",
                    VisitDuration = CalculateVisitDuration(vp.EntryTime.Value)
                })
                .ToListAsync();

            return Json(visitors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching active visitors");
            return StatusCode(500, new { message = "Error fetching active visitors" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetTodayExits()
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var today = DateTime.UtcNow.Date;
            var exits = await _context.VisitorPasses
                .Include(vp => vp.Homeowner)
                .Where(vp => vp.ExitTime.HasValue && vp.ExitTime.Value.Date == today)
                .OrderByDescending(vp => vp.ExitTime)
                .Select(vp => new
                {
                    vp.Id,
                    vp.VisitorName,
                    EntryTime = vp.EntryTime.HasValue ? vp.EntryTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : "N/A",
                    ExitTime = vp.ExitTime.Value.ToString("yyyy-MM-dd HH:mm:ss"),
                    vp.Purpose,
                    HomeownerName = vp.Homeowner != null ? $"{vp.Homeowner.FirstName} {vp.Homeowner.LastName}" : "N/A"
                })
                .ToListAsync();

            return Json(exits);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching today's exits");
            return StatusCode(500, new { message = "Error fetching today's exits" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllVehicleRegistrations()
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var registrations = await _context.VehicleRegistrations
                .Include(vr => vr.Homeowner)
                .OrderByDescending(vr => vr.RegistrationDate)
                .Select(vr => new
                {
                    vr.Id,
                    vr.VehicleMake,
                    vr.VehicleModel,
                    vr.PlateNumber,
                    RegistrationDate = vr.RegistrationDate.ToString("yyyy-MM-dd"),
                    vr.Status,
                    HomeownerName = vr.Homeowner != null ? $"{vr.Homeowner.FirstName} {vr.Homeowner.LastName}" : "N/A"
                })
                .ToListAsync();

            return Json(registrations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all vehicle registrations");
            return StatusCode(500, new { message = "Error fetching all vehicle registrations" });
        }
    }

    private string CalculateVisitDuration(DateTime entryTime)
    {
        var duration = DateTime.UtcNow - entryTime;
        if (duration.TotalDays >= 1)
        {
            return $"{Math.Floor(duration.TotalDays)} days";
        }
        else if (duration.TotalHours >= 1)
        {
            return $"{Math.Floor(duration.TotalHours)} hours";
        }
        else
        {
            return $"{Math.Floor(duration.TotalMinutes)} minutes";
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateVisitorPassStatus([FromBody] UpdateVisitorPassStatusDto model)
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var pass = await _context.VisitorPasses.FindAsync(model.PassId);
            if (pass == null)
                return NotFound(new { success = false, message = "Visitor pass not found" });

            pass.Status = model.Action == "approve" ? "Approved" : 
                         model.Action == "reject" ? "Rejected" : 
                         model.Action == "revoke" ? "Revoked" : pass.Status;

            if (model.Action == "approve")
            {
                pass.EntryTime = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                message = $"Visitor pass {model.Action}d successfully",
                passDetails = new {
                    pass.Id,
                    pass.VisitorName,
                    EntryTime = pass.EntryTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                    pass.Purpose,
                    HomeownerName = pass.Homeowner != null ? $"{pass.Homeowner.FirstName} {pass.Homeowner.LastName}" : "N/A"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating visitor pass status");
            return StatusCode(500, new { success = false, message = "Error updating visitor pass status" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateVehicleRegistrationStatus([FromBody] UpdateVehicleRegistrationStatusDto model)
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var registration = await _context.VehicleRegistrations.FindAsync(model.RegistrationId);
            if (registration == null)
                return NotFound(new { success = false, message = "Vehicle registration not found" });

            registration.Status = model.Action == "approve" ? "Approved" : 
                                model.Action == "reject" ? "Rejected" : 
                                model.Action == "revoke" ? "Revoked" : registration.Status;

            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                message = $"Vehicle registration {model.Action}d successfully" 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating vehicle registration status");
            return StatusCode(500, new { success = false, message = "Error updating vehicle registration status" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordVisitorExit([FromBody] RecordVisitorExitDto model)
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var pass = await _context.VisitorPasses.FindAsync(model.PassId);
            if (pass == null)
                return NotFound(new { success = false, message = "Visitor pass not found" });

            pass.ExitTime = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                message = "Visitor exit recorded successfully",
                exitDetails = new {
                    pass.VisitorName,
                    EntryTime = pass.EntryTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                    ExitTime = pass.ExitTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                    pass.Purpose,
                    HomeownerName = pass.Homeowner != null ? $"{pass.Homeowner.FirstName} {pass.Homeowner.LastName}" : "N/A"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording visitor exit");
            return StatusCode(500, new { success = false, message = "Error recording visitor exit" });
        }
    }

    public class UpdateVisitorPassStatusDto
    {
        public int PassId { get; set; }
        public string Action { get; set; } = string.Empty;
    }

    public class UpdateVehicleRegistrationStatusDto
    {
        public int RegistrationId { get; set; }
        public string Action { get; set; } = string.Empty;
    }

    public class RecordVisitorExitDto
    {
        public int PassId { get; set; }
    }

    [HttpGet]
    public IActionResult GetPendingHomeowners()
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var pendingHomeowners = _context.Homeowners
                .Where(h => h.Status.ToLower() == "pending")
                .Select(h => new
                {
                    h.Id,
                    h.FirstName,
                    h.MiddleName,
                    h.LastName,
                    h.Phase,
                    h.Block,
                    h.HouseNumber,
                    h.Status
                })
                .ToList();

            return Json(pendingHomeowners);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching pending homeowners");
            return StatusCode(500, new { message = "Error fetching pending homeowners" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddHomeownerLog([FromBody] HomeownerLog log)
    {
        try
        {
            if (log == null)
            {
                return Json(new { success = false, message = "Invalid log data" });
            }

            // Set the current date if not provided
            if (log.Date == default)
            {
                log.Date = DateTime.Now;
            }

            _context.HomeownerLogs.Add(log);
            await _context.SaveChangesAsync();
            
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetPendingHomeownerCount()
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var count = await _context.Homeowners
                .CountAsync(h => h.Status == "Pending");
            return Json(new { count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching pending homeowner count");
            return StatusCode(500, new { message = "Error fetching pending homeowner count" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetPendingVisitorCount()
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var count = await _context.VisitorPasses
                .CountAsync(v => v.Status == "Pending");
            return Json(new { count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching pending visitor count");
            return StatusCode(500, new { message = "Error fetching pending visitor count" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetPendingFacilityCount()
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var count = await _context.FacilityReservations
                .CountAsync(f => f.Status == "Pending");
            return Json(new { count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching pending facility count");
            return StatusCode(500, new { message = "Error fetching pending facility count" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetPendingVisitors(int page = 1, int pageSize = 5)
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var totalCount = await _context.VisitorPasses
                .CountAsync(v => v.Status == "Pending");

            var visitors = await _context.VisitorPasses
                .Include(v => v.Homeowner)
                .Where(v => v.Status == "Pending")
                .OrderByDescending(v => v.RequestDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(v => new
                {
                    v.Id,
                    name = v.VisitorName,
                    guestOf = v.Homeowner != null ? $"{v.Homeowner.FirstName} {v.Homeowner.LastName}" : "N/A",
                    purpose = v.Purpose,
                    date = v.VisitDate
                })
                .ToListAsync();

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            return Json(new
            {
                visitors,
                totalCount,
                totalPages,
                startIndex = (page - 1) * pageSize,
                endIndex = Math.Min(page * pageSize, totalCount)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching pending visitors");
            return StatusCode(500, new { message = "Error fetching pending visitors" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetPendingFacilityBookings(int page = 1, int pageSize = 5)
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var totalCount = await _context.FacilityReservations
                .CountAsync(f => f.Status == "Pending");

            var bookings = await _context.FacilityReservations
                .Include(f => f.Facility)
                .Include(f => f.Homeowner)
                .Where(f => f.Status == "Pending")
                .OrderByDescending(f => f.DateSubmitted)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(f => new
                {
                    f.Id,
                    place = f.Facility.Name,
                    dateOfUsage = f.DateSubmitted,
                    purpose = f.Purpose,
                    reservedBy = f.Homeowner != null ? $"{f.Homeowner.FirstName} {f.Homeowner.LastName}" : "N/A"
                })
                .ToListAsync();

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            return Json(new
            {
                bookings,
                totalCount,
                totalPages,
                startIndex = (page - 1) * pageSize,
                endIndex = Math.Min(page * pageSize, totalCount)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching pending facility bookings");
            return StatusCode(500, new { message = "Error fetching pending facility bookings" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateVisitorStatus([FromBody] UpdateVisitorStatusDto model)
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var visitor = await _context.VisitorPasses.FindAsync(model.VisitorId);
            if (visitor == null)
                return NotFound(new { success = false, message = "Visitor not found" });

            visitor.Status = model.Status;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Visitor status updated to {model.Status}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating visitor status");
            return StatusCode(500, new { success = false, message = "Error updating visitor status" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateFacilityBookingStatus([FromBody] UpdateFacilityBookingStatusDto model)
    {
        if (!IsStaffLoggedIn(out _))
            return Unauthorized();

        try
        {
            var booking = await _context.FacilityReservations.FindAsync(model.BookingId);
            if (booking == null)
                return NotFound(new { success = false, message = "Booking not found" });

            booking.Status = model.Status;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Booking status updated to {model.Status}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating facility booking status");
            return StatusCode(500, new { success = false, message = "Error updating facility booking status" });
        }
    }

    public class UpdateVisitorStatusDto
    {
        public int VisitorId { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class UpdateFacilityBookingStatusDto
    {
        public int BookingId { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}

}
