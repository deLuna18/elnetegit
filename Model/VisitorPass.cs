using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubdivisionManagement.Model
{
    public class VisitorPass
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string VisitorName { get; set; } = string.Empty;

        [Required]
        public DateTime VisitDate { get; set; }

        [Required]
        public string Purpose { get; set; } = string.Empty;

        [Required]
        public string Status { get; set; } = "Pending";

        public DateTime RequestDate { get; set; } = DateTime.UtcNow;

        public DateTime? ApprovalDate { get; set; }
        
        public DateTime? EntryTime { get; set; }
        
        public DateTime? ExitTime { get; set; }

        [ForeignKey("Homeowner")]
        public int HomeownerId { get; set; }
        
        public virtual Homeowner? Homeowner { get; set; }
    }
} 