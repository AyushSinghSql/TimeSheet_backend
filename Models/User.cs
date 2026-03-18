using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TimeSheet.Models.YourNamespace.Models;

namespace TimeSheet.Models
{
    public class User
    {
        public int UserId { get; set; }
        public string Username { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;
        public string Role { get; set; } = null!;
        public bool IsActive { get; set; } = true;
        public bool FirstLogin { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        [NotMapped]
        public List<Project>? Projects { get; set; }   

        [NotMapped]
        public string? ProjectName { get; set; }

        [NotMapped]
        public string? ProjecId { get; set; }
        public ICollection<UserBackup> Backups { get; set; } = new List<UserBackup>();
        public ICollection<ApprovalRequest> ApprovalRequests { get; set; } = new List<ApprovalRequest>();
    }

    [Table("user_backups")]
    public class UserBackup
    {
        [Key]
        [ForeignKey("User")]
        [Column("user_id")]
        public int UserId { get; set; }

        [Key]
        [ForeignKey("BackupUser")]
        [Column("backup_user_id")]
        public int BackupUserId { get; set; }

        public User? User { get; set; }
        public User? BackupUser { get; set; }
    }
}


