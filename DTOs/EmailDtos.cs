namespace TimeSheet.DTOs
{
    public class DailySummaryResponse
    {
        public string Date { get; set; }
        public int TotalApproved { get; set; }
        public int TotalPending { get; set; }
        public int TotalRejected { get; set; }

        public List<BatchStatus> ApprovedBatchStatus { get; set; }
        public List<BatchStatus> RejectedBatchStatus { get; set; }
        public List<BatchStatus> PendingBatchStatus { get; set; }

        public List<ApprovalSummaryDtos> ApprovedByApprover { get; set; }
        public List<ApprovalSummaryDtos> RejectedByApprover { get; set; }
        public List<PendingSummary> PendingByApprover { get; set; }
    }

    public class ApprovalSummary
    {
        public string BatchId { get; set; }
        public string ApproverName { get; set; }
        public int Level { get; set; }
        public int Count { get; set; }
    }
    public class ApprovalSummaryDtos
    {

        public string BatchId { get; set; }
        public List<ApprovalSummary> approvalSummaries { get; set; }
    }

    public class BatchStatus
    {

        public string BatchId { get; set; }
        public int Count { get; set; }
    }
    public class PendingSummary
    {
        public string BatchId { get; set; }
        public int UserId { get; set; }
        public int Level { get; set; }
        public int Count { get; set; }
        public List<UserSummary> Approvers { get; set; }
    }

    public class UserSummary
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string FullName { get; set; }
    }

}
