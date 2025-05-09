using SubdivisionManagement.Model;

namespace SubdivisionManagement.Model
{
    public class ContactRequest
    {
        public int Id { get; set; }
        public int HomeownerId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string QueryType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime DateSubmitted { get; set; } = DateTime.Now;
        public string Status { get; set; } = "Pending";
        
        // Navigation property
        public virtual Homeowner? Homeowner { get; set; }
    }
}