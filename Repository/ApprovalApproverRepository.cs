using Microsoft.EntityFrameworkCore;
using TimeSheet.Models;

namespace TimeSheet.Repository
{
    public interface IApprovalApproverRepository
    {
        Task<IEnumerable<ApprovalApprover>> GetByWorkflowAsync(int workflowId);
        Task AddAsync(ApprovalApprover approver);
        Task RemoveAsync(int approverId);
        Task<bool> IsUserApproverAsync(int workflowId, int userId);
    }
    public class ApprovalApproverRepository : IApprovalApproverRepository
    {
        private readonly AppDbContext _context;

        public ApprovalApproverRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<ApprovalApprover>> GetByWorkflowAsync(int workflowId)
        {
            return await _context.ApprovalApprovers
                .Where(a => a.WorkflowId == workflowId && a.IsActive)
                .ToListAsync();
        }

        public async Task AddAsync(ApprovalApprover approver)
        {
            _context.ApprovalApprovers.Add(approver);
            await _context.SaveChangesAsync();
        }

        public async Task RemoveAsync(int approverId)
        {
            var approver = await _context.ApprovalApprovers.FindAsync(approverId);
            if (approver != null)
            {
                approver.IsActive = false; // Soft delete
                await _context.SaveChangesAsync();
            }
        }

        public async Task<bool> IsUserApproverAsync(int workflowId, int userId)
        {
            return await _context.ApprovalApprovers
                .AnyAsync(a => a.WorkflowId == workflowId && a.UserId == userId && a.IsActive);
        }
    }

}
