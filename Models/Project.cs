using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TimeSheet.Models
{
    namespace YourNamespace.Models
    {
        [Table("project", Schema = "public")]
        public class Project
        {
            [Key]
            [Column("project_id")]
            [MaxLength(50)]
            public string ProjectId { get; set; } = null!;  // Project (unique code/id)

            [Column("project_name")]
            [MaxLength(255)]
            [Required]
            public string ProjectName { get; set; } = null!;

            [Column("project_type")]
            [MaxLength(50)]
            [Required]
            public string ProjectType { get; set; } = null!;

            [Column("owning_org_id")]
            [MaxLength(50)]
            [Required]
            public string OwningOrgId { get; set; } = null!;

            [Column("project_manager_id")]
            public string? ProjectManagerId { get; set; }   // nullable

            [Column("project_manager_name")]
            [MaxLength(255)]
            public string? ProjectManagerName { get; set; }
            [Column("email")]
            [MaxLength(50)]
            public string? Email { get; set; } = null!;
            [Column("status")]
            [MaxLength(50)]
            public string? Status { get; set; } = null!;
        }
    }

}
