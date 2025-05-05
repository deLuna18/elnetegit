using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubdivisionManagement.Model
{
    public class Billing
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int HomeownerId { get; set; } // Foreign key to Homeowner

        [ForeignKey("HomeownerId")]
        public virtual Homeowner? Homeowner { get; set; }

        [Required]
        public DateTime Date { get; set; } 

        [Required]
        [MaxLength(100)]
        public string Type { get; set; } = string.Empty; // e.g., Water, Electricity, Maintenance, etc.

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Pending"; // e.g., Paid, Pending

        public string? Note { get; set; } // Optional note  for staff when approving or disapproving the reservation

        [MaxLength(20)]
        public string? PaymentMethod { get; set; } // e.g., Credit Card, GCash, etc.

        [MaxLength(50)]
        public string? LastUpdatedBy { get; set; } // Stores StaffId as string or 'admin'

        [MaxLength(255)]
        public string? ReceiptPath { get; set; } // Optional field to store the path of uploaded receipt
    }
}