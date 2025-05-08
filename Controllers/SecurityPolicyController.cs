using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubdivisionManagement.Model;
using System.Threading.Tasks;

namespace SubdivisionManagement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SecurityPolicyController : ControllerBase
    {
        private readonly HomeContext _context;

        public SecurityPolicyController(HomeContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<SecurityPolicy>> GetSecurityPolicy()
        {
            var policy = await _context.SecurityPolicies.FirstOrDefaultAsync();
            if (policy == null)
            {
                return NotFound();
            }

            return policy;
        }

        [HttpGet("statistics")]
        public async Task<ActionResult<DashboardStatistics>> GetDashboardStatistics()
        {
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);

            var statistics = new DashboardStatistics
            {
                TotalVisitorsToday = await _context.VisitorPasses
                    .CountAsync(v => v.VisitDate.Date == today),

                ActivePasses = await _context.VisitorPasses
                    .CountAsync(v => v.Status == "Active" || v.Status == "Approved"),

                RegisteredVehicles = await _context.VehicleRegistrations
                    .CountAsync(v => v.Status == "Active" || v.Status == "Approved"),

                SecurityAlerts = await _context.VisitorPasses
                    .CountAsync(v => v.Status == "Denied" || v.Status == "Blacklisted")
            };

            // Calculate percentage changes
            var yesterday = today.AddDays(-1);
            var lastWeek = today.AddDays(-7);
            var lastMonth = today.AddMonths(-1);

            var yesterdayVisitors = await _context.VisitorPasses
                .CountAsync(v => v.VisitDate.Date == yesterday);

            var lastWeekAlerts = await _context.VisitorPasses
                .CountAsync(v => v.VisitDate >= lastWeek && (v.Status == "Denied" || v.Status == "Blacklisted"));

            var lastMonthVehicles = await _context.VehicleRegistrations
                .CountAsync(v => v.RegistrationDate >= lastMonth && (v.Status == "Active" || v.Status == "Approved"));

            statistics.VisitorChangePercent = CalculatePercentageChange(yesterdayVisitors, statistics.TotalVisitorsToday);
            statistics.SecurityAlertsChangePercent = CalculatePercentageChange(lastWeekAlerts / 7, statistics.SecurityAlerts);
            statistics.VehiclesChangePercent = CalculatePercentageChange(lastMonthVehicles, statistics.RegisteredVehicles);

            return statistics;
        }

        private double CalculatePercentageChange(double oldValue, double newValue)
        {
            if (oldValue == 0) return newValue > 0 ? 100 : 0;
            return Math.Round(((newValue - oldValue) / oldValue) * 100, 1);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateSecurityPolicy(SecurityPolicy policy)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var existingPolicy = await _context.SecurityPolicies.FirstOrDefaultAsync();
            if (existingPolicy == null)
            {
                return NotFound();
            }

            // Update the existing policy
            existingPolicy.MaxVisitDuration = policy.MaxVisitDuration;
            existingPolicy.AdvanceNoticeRequired = policy.AdvanceNoticeRequired;
            existingPolicy.MaxActivePassesPerHomeowner = policy.MaxActivePassesPerHomeowner;
            existingPolicy.VehicleRegistrationValidityMonths = policy.VehicleRegistrationValidityMonths;
            existingPolicy.RequirePhotoId = policy.RequirePhotoId;
            existingPolicy.EnableAutoApprovalForRegularVisitors = policy.EnableAutoApprovalForRegularVisitors;
            existingPolicy.LastModified = DateTime.UtcNow;
            existingPolicy.ModifiedBy = User.Identity?.Name ?? "Unknown";

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SecurityPolicyExists(policy.Id))
                {
                    return NotFound();
                }
                throw;
            }

            return NoContent();
        }

        private bool SecurityPolicyExists(int id)
        {
            return _context.SecurityPolicies.Any(e => e.Id == id);
        }
    }

    public class DashboardStatistics
    {
        public int TotalVisitorsToday { get; set; }
        public int ActivePasses { get; set; }
        public int RegisteredVehicles { get; set; }
        public int SecurityAlerts { get; set; }
        public double VisitorChangePercent { get; set; }
        public double SecurityAlertsChangePercent { get; set; }
        public double VehiclesChangePercent { get; set; }
    }
} 