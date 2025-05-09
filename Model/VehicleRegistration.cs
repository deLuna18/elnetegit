using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubdivisionManagement.Model
{
    public class VehicleRegistration
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string VehicleMake { get; set; } = string.Empty;

        [Required]
        public string VehicleModel { get; set; } = string.Empty;

        [Required]
        public string PlateNumber { get; set; } = string.Empty;

        public string Status { get; set; } = "Pending";

        public DateTime RegistrationDate { get; set; } = DateTime.UtcNow;

        public DateTime? ApprovalDate { get; set; }

        [ForeignKey("Homeowner")]
        public int HomeownerId { get; set; }
        public virtual Homeowner? Homeowner { get; set; }
    }
}