using System.ComponentModel.DataAnnotations.Schema;

namespace TimeSheet.Models
{
    [Table("employee")]
    public class Employee
    {
        [Column("employeeid")]
        public string EmployeeId { get; set; } = null!;

        [Column("displayedname")]
        public string DisplayedName { get; set; } = null!;

        [Column("lastname")]
        public string? LastName { get; set; }

        [Column("firstname")]
        public string? FirstName { get; set; }

        [Column("status")]
        public string? Status { get; set; }

        [Column("createddate")]
        public DateTime? CreatedDate { get; set; }

        [Column("modifieddate")]
        public DateTime? ModifiedDate { get; set; }

        [Column("createdby")]
        public string? CreatedBy { get; set; }

        [Column("modifiedby")]
        public string? ModifiedBy { get; set; }

        // Navigation property
        public ICollection<Timesheet> Timesheets { get; set; } = new List<Timesheet>();
    }
}
