using Microsoft.EntityFrameworkCore;

namespace SubdivisionManagement.Model
{
    public class HomeContext : DbContext
    {
        public HomeContext(DbContextOptions<HomeContext> options) : base(options) { }

        public DbSet<Homeowner> Homeowners { get; set; } = null!;
        public DbSet<Admin> Admins { get; set; }
        public DbSet<Staff> Staffs { get; set; }  
        public DbSet<Announcement> Announcements { get; set; }
        public DbSet<ServiceRequest> ServiceRequests { get; set; }
        public DbSet<ContactRequest> ContactRequests { get; set; }
        public DbSet<Facility> Facilities { get; set; } // Ensure this line exists
        public DbSet<FacilityReservation> FacilityReservations { get; set; } // Add this line
        public DbSet<Billing> Billings { get; set; } // Add Billing table

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Existing service request configurations...
            modelBuilder.Entity<ServiceRequest>()
                .HasOne(sr => sr.Homeowner)
                .WithMany()
                .HasForeignKey(sr => sr.HomeownerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ServiceRequest>()
                .HasOne(sr => sr.Staff)
                .WithMany()
                .HasForeignKey(sr => sr.StaffId)
                .OnDelete(DeleteBehavior.SetNull);

            // Add ContactRequest configuration
            modelBuilder.Entity<ContactRequest>()
                .HasOne(cr => cr.Homeowner)
                .WithMany()
                .HasForeignKey(cr => cr.HomeownerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FacilityReservation>()
                .HasOne(fr => fr.Homeowner)
                .WithMany()
                .HasForeignKey(fr => fr.HomeownerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Billing>()
                .HasOne(b => b.Homeowner)
                .WithMany()
                .HasForeignKey(b => b.HomeownerId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}

