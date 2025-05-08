using System;
using System.ComponentModel.DataAnnotations;

namespace SubdivisionManagement.Model
{
    public class SecurityPolicy
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Range(1, 72)]
        public int MaxVisitDuration { get; set; }

        [Required]
        [Range(0, 168)]
        public int AdvanceNoticeRequired { get; set; }

        [Required]
        [Range(1, 10)]
        public int MaxActivePassesPerHomeowner { get; set; }

        [Required]
        [Range(1, 12)]
        public int VehicleRegistrationValidityMonths { get; set; }

        [Required]
        public bool RequirePhotoId { get; set; }

        [Required]
        public bool EnableAutoApprovalForRegularVisitors { get; set; }

        public DateTime LastModified { get; set; }
        public string? ModifiedBy { get; set; }
    }
} 