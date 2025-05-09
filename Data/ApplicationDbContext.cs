using Microsoft.EntityFrameworkCore;
using SubdivisionManagement.Model;

namespace SubdivisionManagement.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Homeowner> Homeowners { get; set; }
        public DbSet<HomeownerLog> HomeownerLogs { get; set; }
    }
} 