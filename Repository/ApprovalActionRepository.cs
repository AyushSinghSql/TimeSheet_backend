using Microsoft.EntityFrameworkCore;
using TimeSheet.Models;

namespace TimeSheet.Repository
{

    public interface IApprovalActionRepository
    {
        Task AddActionAsync(ApprovalAction action);
        Task<bool> IsLevelApprovedAsync(int requestId, int levelNo);
        Task<IEnumerable<ApprovalAction>> GetApprovalsByRequestAndLevelAsync(int requestId, int levelNo);
    }
    public class ApprovalActionRepository : IApprovalActionRepository
    {
        private readonly AppDbContext _context;

        public ApprovalActionRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddActionAsync(ApprovalAction action)
        {
            _context.ApprovalActions.Add(action);
            await _context.SaveChangesAsync();
        }

        public async Task<bool> IsLevelApprovedAsync(int requestId, int levelNo)
        {
            return await _context.ApprovalActions
                .AnyAsync(a => a.RequestId == requestId && a.LevelNo == levelNo && a.ActionStatus == "APPROVED");
        }
        public async Task<IEnumerable<ApprovalAction>> GetApprovalsByRequestAndLevelAsync(int requestId, int levelNo)
        {
            return await _context.ApprovalActions
                .Where(a => a.RequestId == requestId && a.LevelNo == levelNo)
                .ToListAsync();
        }
    }
}
