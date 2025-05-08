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
         public DbSet<ServiceCategory> ServiceCategories { get; set; }
        public DbSet<ContactRequest> ContactRequests { get; set; }
        public DbSet<Facility> Facilities { get; set; } // Ensure this line exists
        public DbSet<FacilityReservation> FacilityReservations { get; set; } // Add this line
        public DbSet<Billing> Billings { get; set; } // Add Billing table
         public DbSet<VisitorPass> VisitorPasses { get; set; }
        public DbSet<VehicleRegistration> VehicleRegistrations { get; set; }
        public DbSet<SecurityPolicy> SecurityPolicies { get; set; }

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

             // Add ServiceCategory configuration
            modelBuilder.Entity<ServiceCategory>()
                .HasIndex(sc => sc.Name)
                .IsUnique();

            modelBuilder.Entity<VisitorPass>()
                .HasOne(v => v.Homeowner)
                .WithMany()
                .HasForeignKey(v => v.HomeownerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<VehicleRegistration>()
                .HasOne(v => v.Homeowner)
                .WithMany()
                .HasForeignKey(v => v.HomeownerId)
                .OnDelete(DeleteBehavior.Cascade);

         // Add default security policy if none exists
            modelBuilder.Entity<SecurityPolicy>().HasData(
                new SecurityPolicy
                {
                    Id = 1,
                    MaxVisitDuration = 24,
                    AdvanceNoticeRequired = 2,
                    MaxActivePassesPerHomeowner = 5,
                    VehicleRegistrationValidityMonths = 12,
                    RequirePhotoId = true,
                    EnableAutoApprovalForRegularVisitors = false,
                    LastModified = new DateTime(2024, 1, 1),
                    ModifiedBy = "System"
                }
            );
            
        }
    }
}

