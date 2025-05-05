using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubdivisionManagement.Model
{
    public class Facility
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string Name { get; set; } = string.Empty;
        [Required]
        public string Description { get; set; } = string.Empty;
        [Required]
        public int Capacity { get; set; }
        [Required]
        public string? ImagePath { get; set; }
        [Required]
        public DateTime DateCreated { get; set; } = DateTime.Now;

        // New Status property
        [Required]
        public string Status { get; set; } = "Available";
    }
}