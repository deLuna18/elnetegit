using System.ComponentModel.DataAnnotations;

namespace SubdivisionManagement.Model
{
    public class Staff
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required]
        public string Position { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string ContactNumber { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        public string Username { get; set; } = string.Empty;

        public string? PasswordSalt { get; set; }

        [Required]
        public DateTime Date_Hired { get; set; }

        [Required]
        public string Department { get; set; } = string.Empty;
        
         // Additional properties for service functionality
        [MaxLength(20)]
        public string Status { get; set; } = "Active";

        [MaxLength(50)]
        public string Role { get; set; } = "Staff";

        [MaxLength(100)]
        public string? Specialization { get; set; }
        // Add parameterless constructor for Entity Framework
        public Staff()
        {
        }

        // Default constructor: PasswordSalt will be assigned during registration
        public Staff(string fullName, string position, string email, string contactNumber, string passwordHash, string username, DateTime dateHired, string department)
        {
            FullName = fullName;
            Position = position;
            Email = email;
            ContactNumber = contactNumber;
            PasswordHash = passwordHash;
            Username = username;
            Date_Hired = dateHired;
            Department = department;
            Status = "Active";
            Role = "Staff";
        }

    }
}
