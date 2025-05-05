using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubdivisionManagement.Model
{
    public class FacilityReservation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int FacilityId { get; set; } // Foreign key to Facilities table

        [ForeignKey("FacilityId")]
        public virtual Facility? Facility { get; set; } // Navigation property for Facility

        [Required]
        [MaxLength(100)]
        public string FacilityName { get; set; } = string.Empty; // Facility name from Facilities table

        [Required]
        public string Purpose { get; set; } = string.Empty;

        [Required]
        public DateTime DateSubmitted { get; set; } = DateTime.UtcNow; // Current date

        [Required]
        public DateTime ReservationDate { get; set; } // Date of reservation

        [Required]
        public TimeSpan ReservationTimeStart { get; set; } // Start time of reservation

        [Required]
        public TimeSpan ReservationTimeEnd { get; set; } // End time of reservation

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Pending"; // Default status is "Pending"

        public string? Note { get; set; } // Optional note  for staff when approving or disapproving the reservation

        [Required]
        public int HomeownerId { get; set; } // Foreign key to Homeowner

        [ForeignKey("HomeownerId")]
        public virtual Homeowner? Homeowner { get; set; } // Navigation property for Homeowner

        // New column to track who last updated the reservation (StaffId or 'admin')
        [MaxLength(50)]
        public string? LastUpdatedBy { get; set; } // Stores StaffId as string or 'admin'
    }
}