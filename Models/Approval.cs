using System.ComponentModel.DataAnnotations.Schema;

namespace TimeSheet.Models
{
    public class ApprovalRequest
    {
        public int RequestId { get; set; }
        public int TimesheetId { get; set; }

        public string RequestType { get; set; } = null!;
        public int RequesterId { get; set; }
        public string? RequestData { get; set; }
        public string Status { get; set; } = "PENDING";
        public Boolean IsExported { get; set; } = false;

        public int CurrentLevelNo { get; set; } = 1;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(TimesheetId))]
        public Timesheet Timesheet { get; set; } = null!;

        [ForeignKey(nameof(RequesterId))]
        public User Requester { get; set; } = null!;

        public ICollection<ApprovalAction> Actions { get; set; }

    }

    public class ApprovalWorkflow
    {
        public int WorkflowId { get; set; }
        public string RequestType { get; set; } = null!;
        public int LevelNo { get; set; }
        public string ApproverRole { get; set; } = null!;
        public Boolean IsMandetory { get; set; } = false;
    }

    public class ApprovalAction
    {
        public int ActionId { get; set; }
        public int RequestId { get; set; }
        public int LevelNo { get; set; }
        public int ApproverId { get; set; }
        public string ActionStatus { get; set; } = null!;
        public string? ActionComment { get; set; }
        public DateTime ActionDate { get; set; } = DateTime.UtcNow;
        public string IpAddress { get; set; } = null!;

        public ApprovalRequest Request { get; set; }
    }

    public class ApprovalApprover
    {
        public int ApproverId { get; set; }
        public int WorkflowId { get; set; }
        public int UserId { get; set; }
        public bool IsActive { get; set; } = true;

        public ApprovalWorkflow Workflow { get; set; }
    }


}
