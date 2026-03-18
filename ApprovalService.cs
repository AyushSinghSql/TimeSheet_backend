using Microsoft.EntityFrameworkCore;
using TimeSheet.DTOs;
using TimeSheet.Models;
using TimeSheet.Repository;

namespace TimeSheet
{

    public interface IApprovalService
    {
        Task RejectAsync(int requestId, int levelNo, int approverUserId, string comment, string ipaddress);
        Task ApproveAsync(int requestId, int levelNo, int approverUserId, string comment, string ipaddress);
    }
    public class ApprovalService : IApprovalService
    {
        private readonly IApprovalRequestRepository _requestRepo;
        private readonly IApprovalActionRepository _actionRepo;
        private readonly IApprovalApproverRepository _approverRepo;

        public ApprovalService(
            IApprovalRequestRepository requestRepo,
            IApprovalActionRepository actionRepo,
            IApprovalApproverRepository approverRepo)
        {
            _requestRepo = requestRepo;
            _actionRepo = actionRepo;
            _approverRepo = approverRepo;
        }

        public async Task ApproveAsync(int requestId, int levelNo, int approverUserId, string comment, string ipaddress)
        {
            var request = await _requestRepo.GetByIdAsync(requestId)
                ?? throw new Exception("Request not found.");

            var workflow = await _requestRepo.GetWorkflowByTypeAndLevelAsync(request.RequestType, levelNo)
                ?? throw new Exception("Workflow level not found.");

            var isApprover = await _approverRepo.IsUserApproverAsync(workflow.WorkflowId, approverUserId);
            if (!isApprover)
                throw new UnauthorizedAccessException("User is not an approver for this level.");

            var action = new ApprovalAction
            {
                RequestId = requestId,
                LevelNo = levelNo,
                ApproverId = approverUserId,
                ActionStatus = "APPROVED",
                ActionComment = comment,
                IpAddress = ipaddress
            };
            await _actionRepo.AddActionAsync(action);

            var approvers = await _approverRepo.GetByWorkflowAsync(workflow.WorkflowId);
            var approvalsAtLevel = await _actionRepo.GetApprovalsByRequestAndLevelAsync(requestId, levelNo);

            bool allApproved = approvers.Any(a =>
                approvalsAtLevel.Any(ap => ap.ApproverId == a.UserId && ap.ActionStatus == "APPROVED")
            );

            if (allApproved)
            {
                var nextLevel = await _requestRepo.GetWorkflowByTypeAndLevelAsync(request.RequestType, levelNo + 1);

                if (nextLevel != null)
                {
                    request.CurrentLevelNo = levelNo + 1;
                    await _requestRepo.UpdateAsync(request);
                }
                else
                {
                    request.Status = "APPROVED";
                    request.CurrentLevelNo = 0;
                    await _requestRepo.UpdateAsync(request);
                }
            }
        }
        public async Task RejectAsync(int requestId, int levelNo, int approverUserId, string comment, string ipaddress)
        {
            var request = await _requestRepo.GetByIdAsync(requestId)
                ?? throw new Exception("Request not found.");

            var workflow = await _requestRepo.GetWorkflowByTypeAndLevelAsync(request.RequestType, levelNo)
                ?? throw new Exception("Workflow level not found.");

            var isApprover = await _approverRepo.IsUserApproverAsync(workflow.WorkflowId, approverUserId);
            if (!isApprover)
                throw new UnauthorizedAccessException("User is not an approver for this level.");

            var action = new ApprovalAction
            {
                RequestId = requestId,
                LevelNo = levelNo,
                ApproverId = approverUserId,
                ActionStatus = "REJECTED",
                ActionComment = comment,
                IpAddress = ipaddress
            };
            await _actionRepo.AddActionAsync(action);

            request.Status = "REJECTED";
            request.CurrentLevelNo = 0;
            await _requestRepo.UpdateAsync(request);
        }

    }


}
