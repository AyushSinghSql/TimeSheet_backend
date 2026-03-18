using Microsoft.EntityFrameworkCore;
using TimeSheet.Models;

namespace TimeSheet.Repository
{
    public interface IApprovalRequestRepository
    {
        Task<ApprovalRequest> GetByIdAsync(int id);
        Task<IEnumerable<ApprovalRequest>> GetPendingApprovalsForUserAsync(int approverId);
        Task AddAsync(ApprovalRequest request);
        Task UpdateAsync(ApprovalRequest request);
        Task<ApprovalWorkflow?> GetWorkflowByTypeAndLevelAsync(string requestType, int levelNo);
        Task AddRangeAsync(IEnumerable<ApprovalRequest> requests);
    }

    public class ApprovalRequestRepository : IApprovalRequestRepository
    {
        private readonly AppDbContext _context;

        public ApprovalRequestRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<ApprovalRequest> GetByIdAsync(int id)
        {
            return await _context.ApprovalRequests
                .Include(r => r.Timesheet)
                .Include(r => r.Actions)
                .Include(r => r.Requester)
                .FirstOrDefaultAsync(r => r.RequestId == id);
        }

        public async Task<IEnumerable<ApprovalRequest>> GetPendingApprovalsForUserAsync(int approverId)
        {
            var approverRoles = await _context.Users // Assuming a Users table exists
                .Where(u => u.UserId == approverId)
                .Select(u => u.Role)
                .ToListAsync();

            return await _context.ApprovalRequests
                .Where(r => r.Status == "PENDING")
                .Where(r => !_context.ApprovalActions
                    .Any(a => a.RequestId == r.RequestId && approverRoles.Contains(a.Request.RequestType)))
                .ToListAsync();
        }

        public async Task AddAsync(ApprovalRequest request)
        {
            _context.ApprovalRequests.Add(request);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(ApprovalRequest request)
        {
            _context.ApprovalRequests.Update(request);
            await _context.SaveChangesAsync();
        }

        public async Task<ApprovalWorkflow?> GetWorkflowByTypeAndLevelAsync(string requestType, int levelNo)
        {
            return await _context.ApprovalWorkflows
                .FirstOrDefaultAsync(w => w.RequestType == requestType && w.LevelNo == levelNo);
        }

        public async Task AddRangeAsync(IEnumerable<ApprovalRequest> requests)
        {
            await _context.ApprovalRequests.AddRangeAsync(requests);
            await _context.SaveChangesAsync();
        }
    }

}
