using Microsoft.EntityFrameworkCore;
using TimeSheet.Models;
using static TimeSheet.DTOs.UserMappingExtensions;

namespace TimeSheet
{
    public class Helper
    {
        private readonly AppDbContext _context;

        public Helper(AppDbContext context)
        {
            _context = context;
        }

        public List<Timesheet> GetTimeSheets(int userId, string status)
        {
            // base query
            var approvalQuery = _context.ApprovalRequests.Where(p => p.Status == status && p.RequesterId == userId);



            var query = from t in _context.Timesheets
                        join ar in approvalQuery on t.TimesheetId equals ar.TimesheetId
                        join e in _context.Employees on t.EmployeeId equals e.EmployeeId
                        select new
                        {
                            Timesheet = t,
                            RequestId = ar.RequestId,
                            UserName = ar.Requester.Username,
                            ApprovalStatus = ar.Status,
                            EmployeeDisplayName = e.DisplayedName,
                            Notes = t.Notes
                        };

            var result = query
                .OrderBy(x => x.Timesheet.TimesheetDate ?? DateOnly.MaxValue)
                .ToList();

            // map back into Timesheet objects
            var timesheets = result.Select(x =>
            {
                x.Timesheet.RequestId = x.RequestId;
                x.Timesheet.ApprovalStatus = x.ApprovalStatus;
                x.Timesheet.DisplayedName = x.EmployeeDisplayName;
                x.Timesheet.Notes = x.Notes;
                return x.Timesheet;
            }).ToList();

            return timesheets;
        }

        public async Task<LevelDetailsDto> GetUserDetails(string username)
        {
            return await (
                      from u in _context.Users
                      join aa in _context.ApprovalApprovers on u.UserId equals aa.UserId into approverGroup
                      from aa in approverGroup.DefaultIfEmpty()

                      join aw in _context.ApprovalWorkflows on aa.WorkflowId equals aw.WorkflowId into workflowGroup
                      from aw in workflowGroup.DefaultIfEmpty()

                      where u.Username == username
                      select new LevelDetailsDto
                      {
                          UserId = u.UserId,
                          Username = u.Username,
                          FullName = u.FullName,
                          Email = u.Email,
                          Role = u.Role,
                          IsActive = u.IsActive,

                          // Workflow details
                          WorkFlowId = aw != null ? aw.WorkflowId : (int?)null,
                          LevelNo = aw != null ? aw.LevelNo : (int?)null,
                          LevelName = aw != null ? aw.ApproverRole : null,
                      }
                  ).FirstOrDefaultAsync();
        }

        public async Task<List<LevelDetailsDto>> GetDetailsByLevel(int level, List<string> projectids)
        {
            return await (
                      from u in _context.Users
                      join aa in _context.ApprovalApprovers on u.UserId equals aa.UserId into approverGroup
                      from aa in approverGroup.DefaultIfEmpty()

                      join aw in _context.ApprovalWorkflows on aa.WorkflowId equals aw.WorkflowId into workflowGroup
                      from aw in workflowGroup.DefaultIfEmpty()

                          //🔹 Join with Projects to fetch PM details for Level 1
                      join pr in _context.Projects on u.Username equals pr.ProjectManagerId into projectGroup
                      from pr in projectGroup.DefaultIfEmpty()

                      where projectids.Contains(pr.ProjectId) && aw.LevelNo == level && u.IsActive == true
                      select new LevelDetailsDto
                      {
                          UserId = u.UserId,
                          Username = u.Username,
                          FullName = u.FullName,
                          Email = u.Email,
                          Role = u.Role,
                          IsActive = u.IsActive,

                          // Workflow details
                          WorkFlowId = aw != null ? aw.WorkflowId : (int?)null,
                          LevelNo = aw != null ? aw.LevelNo : (int?)null,
                          LevelName = aw != null ? aw.ApproverRole : null,

                          // PM info (only meaningful for Level 1)
                          ProjectId = pr.ProjectId,
                          ProjectManagerId = pr.ProjectManagerId,
                          ProjectName = pr != null && aw.LevelNo == level ? pr.ProjectName : null,
                          ProjectManagerName = pr.ProjectManagerName,
                          ProjectManagerEmail = pr.Email
                      }
                  ).ToListAsync();
        }

        public async Task<List<LevelDetailsDto>> GetBackupUsersByLevel(int level)
        {
            return await (
                      from u in _context.Users
                      join aa in _context.ApprovalApprovers on u.UserId equals aa.UserId into approverGroup
                      from aa in approverGroup.DefaultIfEmpty()

                      join aw in _context.ApprovalWorkflows on aa.WorkflowId equals aw.WorkflowId into workflowGroup
                      from aw in workflowGroup.DefaultIfEmpty()

                          //🔹 Join with Projects to fetch PM details for Level 1
                          //join pr in _context.Projects on u.Username equals pr.ProjectManagerId into projectGroup
                          //from pr in projectGroup.DefaultIfEmpty()

                      where aw.LevelNo == level && u.Role.ToUpper() == "BACKUPUSER" && u.IsActive == true
                      select new LevelDetailsDto
                      {
                          UserId = u.UserId,
                          Username = u.Username,
                          FullName = u.FullName,
                          Email = u.Email,
                          Role = u.Role,
                          IsActive = u.IsActive,

                          // Workflow details
                          WorkFlowId = aw != null ? aw.WorkflowId : (int?)null,
                          LevelNo = aw != null ? aw.LevelNo : (int?)null,
                          LevelName = aw != null ? aw.ApproverRole : null,

                          //// PM info (only meaningful for Level 1)
                          //ProjectId = pr.ProjectId,
                          //ProjectManagerId = pr.ProjectManagerId,
                          //ProjectName = pr != null && aw.LevelNo == level ? pr.ProjectName : null,
                          //ProjectManagerName = pr.ProjectManagerName,
                          //ProjectManagerEmail = pr.Email
                      }
                  ).ToListAsync();
        }

        public async Task<List<LevelDetailsDto>> GetBackupUsersByUser(string UserId)
        {
            return await (
                      from u in _context.Users
                      where u.Username == UserId && (u.Role.ToUpper() == "BACKUPUSER" || u.Role.ToUpper() == "USER") && u.IsActive == true
                      select new LevelDetailsDto
                      {
                          UserId = u.Backups.FirstOrDefault().BackupUser.UserId,
                          Username = u.Backups.FirstOrDefault().BackupUser.Username,
                          FullName = u.Backups.FirstOrDefault().BackupUser.FullName,
                          Email = u.Backups.FirstOrDefault().BackupUser.Email,
                          Role = u.Backups.FirstOrDefault().BackupUser.Role,
                          IsActive = u.Backups.FirstOrDefault().BackupUser.IsActive,

                          // Workflow details
                          WorkFlowId = null,
                          LevelNo = null,
                          LevelName = "",

                          //// PM info (only meaningful for Level 1)
                          //ProjectId = pr.ProjectId,
                          //ProjectManagerId = pr.ProjectManagerId,
                          //ProjectName = pr != null && aw.LevelNo == level ? pr.ProjectName : null,
                          //ProjectManagerName = pr.ProjectManagerName,
                          //ProjectManagerEmail = pr.Email
                      }
                  ).ToListAsync();
        }

        public async Task<List<LevelDetailsDto>> GetUsersByLevel(int level)
        {


            var users = await (
                      from u in _context.Users
                      join aa in _context.ApprovalApprovers on u.UserId equals aa.UserId into approverGroup
                      from aa in approverGroup.DefaultIfEmpty()

                      join aw in _context.ApprovalWorkflows on aa.WorkflowId equals aw.WorkflowId into workflowGroup
                      from aw in workflowGroup.DefaultIfEmpty()

                          //🔹 Join with Projects to fetch PM details for Level 1
                          //join pr in _context.Projects on u.Username equals pr.ProjectManagerId into projectGroup
                          //from pr in projectGroup.DefaultIfEmpty()

                          //where aw.LevelNo == level && (u.Role.ToUpper() == "USER")  && u.IsActive == true
                      where aw.LevelNo == level && u.IsActive == true
                      select new LevelDetailsDto
                      {
                          UserId = u.UserId,
                          Username = u.Username,
                          FullName = u.FullName,
                          Email = u.Email,
                          Role = u.Role,
                          IsActive = u.IsActive,

                          // Workflow details
                          WorkFlowId = aw != null ? aw.WorkflowId : (int?)null,
                          LevelNo = aw != null ? aw.LevelNo : (int?)null,
                          LevelName = aw != null ? aw.ApproverRole : null,
                      }
                  ).ToListAsync();



            return users;
        }

        public async Task<int> MarkAsExportedAsync(List<int> timesheetIds)
        {
            var requests = await _context.ApprovalRequests
                .Where(r => timesheetIds.Contains(r.TimesheetId))
                .ToListAsync();

            foreach (var req in requests)
            {
                req.IsExported = true;
            }

            return await _context.SaveChangesAsync();
        }
    }
}