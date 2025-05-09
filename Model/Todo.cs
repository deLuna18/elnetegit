using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubdivisionManagement.Model
{
    public class Todo
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string Title { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        public bool IsCompleted { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        [Required]
        public int HomeownerId { get; set; }

        [ForeignKey("HomeownerId")]
        public virtual Homeowner? Homeowner { get; set; }
    }
}
