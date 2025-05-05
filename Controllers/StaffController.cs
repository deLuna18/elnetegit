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
}

}
