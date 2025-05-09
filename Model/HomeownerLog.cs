using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubdivisionManagement.Model
{
    public class HomeownerLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Status { get; set; } = string.Empty;  // "Approved" or "Disapproved"

        [Required]
        public DateTime Date { get; set; }

        [Required]
        public int HomeownerId { get; set; }  // Required foreign key to Homeowner

        [ForeignKey("HomeownerId")]
        public virtual Homeowner? Homeowner { get; set; }

        public string? Remarks { get; set; }  // Optional remarks about the approval/disapproval
    }
} 