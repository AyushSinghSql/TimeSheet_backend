namespace TimeSheet.DTOs
{
    public class CreateRequestDto
    {
        public string RequestType { get; set; } = null!;
        public int RequesterId { get; set; }
        public int TimesheetId { get; set; }
        public string? RequestData { get; set; }
        public string ProjectId { get; set; } = null!;
    }
    public class ApprovalActionDto
    {
        public int RequestId { get; set; }
        public int LevelNo { get; set; }
        public int ApproverUserId { get; set; }
        public string Comment { get; set; } = string.Empty;
        public string IpAddress { get; set; } = null;
        public string? Action { get; set; } = null;
    }
    public class BulkApprovalActionDto
    {
        public List<int> RequestIds { get; set; } = new();
        public int ApproverUserId { get; set; }
        public string Action { get; set; } // APPROVED or REJECTED
        public string? Comment { get; set; }
        public string? IpAddress { get; set; }
    }

    public class TimesheetWithRequestDto
    {
        public int TimesheetId { get; set; }
        public DateTime TimesheetDate { get; set; }
        public int? RequestId { get; set; }
        public string? EmployeeDisplayName { get; set; }
    }
    public class ApprovalRequestStatusDto
    {
        public int RequestId { get; set; }
        public string RequestType { get; set; } = string.Empty;
        public string OverallStatus { get; set; } = "PENDING";
        public int CurrentLevelNo { get; set; }
        public List<ApproverInfoDto>? NextApprovers { get; set; }
        public List<ApprovalLevelStatusDto> Levels { get; set; } = new();
        public RejectedInfoDto? RejectedBy { get; set; }
    }

    public class ApprovalLevelStatusDto
    {
        public int LevelNo { get; set; }
        public string LevelName { get; set; } = string.Empty;
        public string Approver { get; set; } = string.Empty;
        public string Action { get; set; } = "PENDING";
        public string? Comment { get; set; }
        public DateTime? ActionDate { get; set; }
    }

    public class ApproverInfoDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Role { get; set; }
    }

    public class RejectedInfoDto
    {
        public string Name { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        public string? Comment { get; set; }
        public DateTime? ActionDate { get; set; }
    }


    //public class TimesheetDto
    //{
    //    public long TimesheetId { get; set; }
    //    public DateTime? TimesheetDate { get; set; }
    //    public string? EmployeeDisplayName { get; set; }
    //    // add other fields you need from Timesheet
    //}

}
