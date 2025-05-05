using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using SubdivisionManagement.Model;
using System.Linq;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System;
using System.Text;
using Microsoft.EntityFrameworkCore;


public class AdminController : Controller
{
    private readonly HomeContext _context;

    public AdminController(HomeContext context)
    {
        _context = context;
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
}
