using EFCore.BulkExtensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.EventLog;
using Npgsql;
using NPOI.SS.Formula.Eval;
using NPOI.SS.Formula.Functions;
using NPOI.XWPF.UserModel;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Ocsp;
using System.Collections.Generic;
using System.Diagnostics;
using TimeSheet;
using TimeSheet.BackgroundQueue;
using TimeSheet.DTOs;
using TimeSheet.Models;
using TimeSheet.Models.YourNamespace.Models;
using TimeSheet.Repository;
using TimeSheet.Services;
using static NPOI.HSSF.Util.HSSFColor;
using static Org.BouncyCastle.Math.EC.ECCurve;
using static TimeSheet.DTOs.UserMappingExtensions;

[ApiController]
[Route("api/[controller]")]
public class ApprovalController : ControllerBase
{
    private readonly IApprovalService _approvalService;
    private readonly IApprovalRequestRepository _requestRepo;
    private readonly AppDbContext _context;
    private readonly EmailService _emailService;
    private readonly Helper _helper;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IConfiguration _config;

    public ApprovalController(IApprovalService approvalService, IApprovalRequestRepository requestRepo, AppDbContext context, EmailService emailService, IBackgroundTaskQueue taskQueue, IConfiguration config)
    {
        _approvalService = approvalService;
        _requestRepo = requestRepo;
        _context = context;
        _emailService = emailService;
        _taskQueue = taskQueue;
        _helper = new Helper(_context);
        _config = config;
    }

    /// <summary>
    /// Create a new request
    /// </summary>
    [HttpPost("requests")]
    public async Task<IActionResult> CreateRequest([FromBody] CreateRequestDto dto)
    {
        var request = new ApprovalRequest
        {
            RequestType = dto.RequestType,
            TimesheetId = dto.TimesheetId,
            RequesterId = dto.RequesterId,
            RequestData = dto.RequestData,
            Status = "PENDING",
            CurrentLevelNo = 1
        };

        await _requestRepo.AddAsync(request);
        return CreatedAtAction(nameof(GetRequestById), new { requestId = request.RequestId }, request);
    }

    [HttpPost("BulkNotify1")]
    public async Task<IActionResult> BulkNofity1([FromBody] List<CreateRequestDto> dtos)
    {
        //var emailService = new EmailService();
        var helper = new Helper(_context);

        //await emailService.SendEmailAsync("vithoba.khot@revolvespl.com", "Project A", "John.doe");

        //return Ok();



        var projectids = dtos.Select(p => p.ProjectId).Distinct().ToList();

        var users = (await helper.GetUsersByLevel(1)).Select(r => new User() { UserId = r.UserId, Email = r.Email, Username = r.Username, FullName = r.FullName });

        var res = await helper.GetDetailsByLevel(2, projectids);
        //        var res = await (
        //    from u in _context.Users
        //    join aa in _context.ApprovalApprovers on u.UserId equals aa.UserId into approverGroup
        //    from aa in approverGroup.DefaultIfEmpty()

        //    join aw in _context.ApprovalWorkflows on aa.WorkflowId equals aw.WorkflowId into workflowGroup
        //    from aw in workflowGroup.DefaultIfEmpty()

        //        //🔹 Join with Projects to fetch PM details for Level 1
        //    join pr in _context.Projects on u.Username equals pr.ProjectManagerId into projectGroup
        //    from pr in projectGroup.DefaultIfEmpty()

        //    where projectids.Contains(pr.ProjectId)
        //    select new
        //    {
        //        UserId = u.UserId,
        //        Username = u.Username,
        //        FullName = u.FullName,
        //        Email = u.Email,
        //        Role = u.Role,
        //        IsActive = u.IsActive,

        //        // Workflow details
        //        WorkFlowId = aw != null ? aw.WorkflowId : (int?)null,
        //        LevelNo = aw != null ? aw.LevelNo : (int?)null,
        //        LevelName = aw != null ? aw.ApproverRole : null,

        //        // PM info (only meaningful for Level 1)
        //        ProjectId = pr.ProjectId,
        //        ProjectManagerId = pr.ProjectManagerId,
        //        ProjectName = pr != null && aw.LevelNo == 1 ? pr.ProjectName : null,
        //        ProjectManagerName = pr.ProjectManagerName,
        //        ProjectManagerEmail = pr.Email
        //    }
        //).ToListAsync();

        var projects = res.Select(p => new Project() { ProjectId = p.ProjectId, ProjectName = p.ProjectName, ProjectManagerName = p.ProjectManagerName, Email = p.ProjectManagerEmail, ProjectManagerId = p.ProjectManagerId }).ToList();
        //var projects = _context.Projects
        //                .Where(p => projectids.Contains(p.ProjectId))
        //                .ToList();
        ////var Users = _context.Users.ToList();
        if (dtos == null || dtos.Count == 0)
            return BadRequest("No requests provided.");

        var requests = new List<ApprovalRequest>();

        foreach (var dto in dtos)
        {

            var value = res.Where(p => p.ProjectId == dto.ProjectId).FirstOrDefault();

            //var manager = projects.FirstOrDefault(p => p.ProjectId == dto.ProjectId);

            //var user = Users.FirstOrDefault(p => p.Username == manager.ProjectManagerId);



            var request = new ApprovalRequest
            {
                RequesterId = value.UserId,
                RequestType = dto.RequestType,
                TimesheetId = dto.TimesheetId,
                //RequesterId = dto.RequesterId,
                RequestData = dto.RequestData,
                Status = "PENDING",
                CurrentLevelNo = 1,
                CreatedAt = DateTime.UtcNow,
                //Requester = user
            };

            requests.Add(request);
        }

        // Perform a bulk UPSERT
        try
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            await _context.BulkInsertOrUpdateAsync(requests, new BulkConfig
            {
                PreserveInsertOrder = true,
                SetOutputIdentity = true,
                UpdateByProperties = new List<string> { "TimesheetId" } // Composite unique key
            });
            stopwatch.Stop();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // Handle unique constraint violations gracefully
        }

        //try
        //{
        //    await _requestRepo.AddRangeAsync(requests);
        //}
        //catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        //{
        //    //throw new InvalidOperationException("Duplicate record detected.");
        //}


        //var userids = requests.Select(p => p.Requester).Distinct().ToList();


        //var levelUsers = await _helper.GetUsersByLevel(1);
        var userids = res.Where(p => requests.Select(q => q.RequesterId).Contains(p.UserId)).Distinct().Select(r => new User() { UserId = r.UserId, Email = r.Email, Username = r.Username, FullName = r.FullName }).ToList();

        string allowRedirect = "false", redirectEmail = string.Empty, emailNotification = "false";

        try
        {
            if (users != null && users.Count() > 0)
            {
                var configs = _context.ConfigValues.ToList();
                if (configs != null && configs.Count > 0)
                {
                    var allowRedirectConfig = configs.FirstOrDefault(c => c.Name == "ALLOW_EMAIL_REDIRECT");
                    if (allowRedirectConfig != null)
                    {
                        allowRedirect = allowRedirectConfig.Value;
                    }
                    var redirectEmailConfig = configs.FirstOrDefault(c => c.Name == "REDIRECT_EMAIL_TO");
                    if (redirectEmailConfig != null)
                    {
                        redirectEmail = redirectEmailConfig.Value;
                    }
                    var emailNotificationConfig = configs.FirstOrDefault(c => c.Name == "EMAIL_NOTIFICATION");
                    if (emailNotificationConfig != null)
                    {
                        emailNotification = emailNotificationConfig.Value;
                    }
                }
            }
            if (!string.IsNullOrEmpty(emailNotification) && emailNotification.ToLower() == "true")
            {
                foreach (var dto in users)
                {
                    var email = dto.Email;
                    var project = projects.Where(p => p.ProjectManagerId == dto.Username).ToList();
                    dto.Projects = project;
                    //dto.ProjectName = project?.ProjectName;
                    //dto.ProjecId = project?.ProjectId;
                    var pendingTimesheets = helper.GetTimeSheets(dto.UserId, "PENDING");

                    if (allowRedirect.ToLower() == "true" && !string.IsNullOrEmpty(redirectEmail))
                    {
                        //await _emailService.SendEmailWithRedirectAsync(email, pendingTimesheets, dto, redirectEmail);
                        await _taskQueue.QueueBackgroundWorkItemAsync(async (sp, ct) =>
                        {
                            //await _emailService.SendEmailWithRedirectAsync(email, pendingTimesheets, dto, redirectEmail);
                            var emailer = sp.GetRequiredService<EmailService>();
                            await emailer.SendEmailWithRedirectAsync(email, pendingTimesheets, dto, redirectEmail);
                        });
                    }
                    else
                    {
                        await _taskQueue.QueueBackgroundWorkItemAsync(async (sp, ct) =>
                        {

                            //await _emailService.SendEmailAsync(email, pendingTimesheets, dto);
                            var emailer = sp.GetRequiredService<EmailService>();
                            await emailer.SendEmailAsync(email, pendingTimesheets, dto);
                        });
                    }

                }
            }

            Console.WriteLine("Emails sent successfully.");
        }
        catch (Exception ex)
        {
            // Log or handle email sending exceptions as needed
            Console.WriteLine("Error while sending Email.");
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);

        }
        return Ok(new
        {
            Message = $"{requests.Count} request(s) created successfully.",
            Requests = requests.Select(r => new { r.RequestId, r.RequestType, r.TimesheetId })
        });
    }



    [HttpPost("BulkNotify")]
    public async Task<IActionResult> BulkNofity([FromBody] List<CreateRequestDto> dtos)
    {
        var helper = new Helper(_context);

        List<Timesheet> timesheets = _context.Timesheets.Where(p => dtos.Select(q => q.TimesheetId).Contains(p.TimesheetId)).Include(t => t.Employee).ToList();

        var projectids = dtos.Select(p => p.ProjectId).Distinct().ToList();

        //var res = await helper.GetDetailsByLevel(2, projectids);
        var res = await helper.GetDetailsByLevel(1, projectids);


        var users = (await helper.GetUsersByLevel(1)).Select(r => new User() { UserId = r.UserId, Email = r.Email, Username = r.Username, FullName = r.FullName });

        users = users.Where(users => res.Select(p => p.UserId).Contains(users.UserId));

        var projects = _context.Projects.Where(p => projectids.Contains(p.ProjectId)).ToList();

        if (dtos == null || dtos.Count == 0)
            return BadRequest("No requests provided.");

        var requests = new List<ApprovalRequest>();

        foreach (var dto in dtos)
        {
            if (res != null && res.Count() > 0)
            {
                var value = res.Where(p => p.ProjectId == dto.ProjectId).FirstOrDefault();

                if (value != null)
                {
                    var request = new ApprovalRequest
                    {
                        RequesterId = value.UserId,
                        RequestType = dto.RequestType,
                        TimesheetId = dto.TimesheetId,
                        RequestData = dto.RequestData,
                        Status = "PENDING",
                        CurrentLevelNo = 1,
                        CreatedAt = DateTime.UtcNow,
                        //Requester = user
                    };
                    requests.Add(request);
                }
                else
                {
                    return BadRequest("No approver found for the provided project IDs. - " + dto.ProjectId);
                }
            }
            else
            {
                return BadRequest("No approver found for the provided project IDs. - " + dto.ProjectId);
            }
        }

        // Perform a bulk UPSERT
        try
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            await _context.BulkInsertOrUpdateAsync(requests, new BulkConfig
            {
                PreserveInsertOrder = true,
                SetOutputIdentity = true,
                UpdateByProperties = new List<string> { "TimesheetId" } // Composite unique key
            });
            stopwatch.Stop();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // Handle unique constraint violations gracefully
        }

        //var userids = res.Where(p => requests.Select(q => q.RequesterId).Contains(p.UserId)).Distinct().Select(r => new User() { UserId = r.UserId, Email = r.Email, Username = r.Username, FullName = r.FullName }).ToList();

        string allowRedirect = "false", redirectEmail = string.Empty, emailNotification = "false", backupUserNotification = "false";

        try
        {
            if (users != null && users.Count() > 0)
            {
                var configs = _context.ConfigValues.ToList();
                if (configs != null && configs.Count > 0)
                {
                    var allowRedirectConfig = configs.FirstOrDefault(c => c.Name == "ALLOW_EMAIL_REDIRECT");
                    if (allowRedirectConfig != null)
                    {
                        allowRedirect = allowRedirectConfig.Value;
                    }
                    var redirectEmailConfig = configs.FirstOrDefault(c => c.Name == "REDIRECT_EMAIL_TO");
                    if (redirectEmailConfig != null)
                    {
                        redirectEmail = redirectEmailConfig.Value;
                    }
                    var emailNotificationConfig = configs.FirstOrDefault(c => c.Name == "EMAIL_NOTIFICATION");
                    if (emailNotificationConfig != null)
                    {
                        emailNotification = emailNotificationConfig.Value;
                    }
                    var backupUserNotificationConfig = configs.FirstOrDefault(c => c.Name == "BACKUP_NOTIFICATION");
                    if (backupUserNotificationConfig != null)
                    {
                        backupUserNotification = backupUserNotificationConfig.Value;
                    }
                }
            }
            if (!string.IsNullOrEmpty(emailNotification) && emailNotification.ToLower() == "true")
            {
                foreach (var dto in users)
                {
                    var userProjects = projects.Where(p => p.ProjectManagerId == dto.Username).Select(u => u.ProjectId).ToList();
                    var email = dto.Email;
                    if (backupUserNotification.ToLower() == "true")
                    {
                        var backupUsers = _context.UserBackups.Where(p => p.UserId == dto.UserId).Include(u => u.BackupUser).ToList();
                        if (backupUsers != null && backupUsers.Count > 0)
                        {
                            var backupUserEmails = backupUsers.Select(p => p.BackupUser.Email).ToList();
                            email += "," + string.Join(",", backupUserEmails);
                        }
                    }
                    dto.Projects = new List<Project>();
                    //dto.ProjectName = project?.ProjectName;
                    //dto.ProjecId = project?.ProjectId;
                    //var pendingTimesheets = helper.GetTimeSheets(dto.UserId, "PENDING");

                    if (allowRedirect.ToLower() == "true" && !string.IsNullOrEmpty(redirectEmail))
                    {
                        //await _emailService.SendEmailWithRedirectAsync(email, pendingTimesheets, dto, redirectEmail);
                        await _taskQueue.QueueBackgroundWorkItemAsync(async (sp, ct) =>
                        {
                            //await _emailService.SendEmailWithRedirectAsync(email, pendingTimesheets, dto, redirectEmail);
                            var emailer = sp.GetRequiredService<EmailService>();
                            await emailer.SendEmailWithRedirectAsync(email, timesheets.Where(p => userProjects.Contains(p.ProjectId)).ToList(), dto, redirectEmail);
                        });
                    }
                    else
                    {
                        await _taskQueue.QueueBackgroundWorkItemAsync(async (sp, ct) =>
                        {
                            //await _emailService.SendEmailAsync(email, pendingTimesheets, dto);
                            var emailer = sp.GetRequiredService<EmailService>();
                            await emailer.SendEmailAsync(email, timesheets.Where(p => userProjects.Contains(p.ProjectId)).ToList(), dto);
                        });
                    }

                }
            }

            Console.WriteLine("Emails sent successfully.");
        }
        catch (Exception ex)
        {
            // Log or handle email sending exceptions as needed
            Console.WriteLine("Error while sending Email.");
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);

        }
        return Ok(new
        {
            Message = $"{requests.Count} request(s) created successfully.",
            Requests = requests.Select(r => new { r.RequestId, r.RequestType, r.TimesheetId })
        });
    }

    /// <summary>
    /// Get request by ID
    /// </summary>
    [HttpGet("requests/{requestId}")]
    public async Task<IActionResult> GetRequestById(int requestId)
    {
        var request = await _requestRepo.GetByIdAsync(requestId);
        if (request == null)
            return NotFound();

        return Ok(request);
    }

    /// <summary>
    /// Approve a request at the current level
    /// </summary>
    [HttpPost("approve")]
    public async Task<IActionResult> Approve([FromBody] ApprovalActionDto dto)
    {
        await _approvalService.ApproveAsync(dto.RequestId, dto.LevelNo, dto.ApproverUserId, dto.Comment, dto.IpAddress);
        return Ok(new { Message = "Request approved successfully." });
    }


    [HttpPost("BulkApprove")]
    public async Task<IActionResult> BulkApprove([FromBody] List<ApprovalActionDto> approvals)
    {
        if (approvals == null || approvals.Count == 0)
            return BadRequest("No approvals provided.");

        foreach (var approval in approvals)
        {
            await _approvalService.ApproveAsync(approval.RequestId, approval.LevelNo, approval.ApproverUserId, approval.Comment, approval.IpAddress);
        }

        return Ok(new { Message = $"{approvals.Count} request(s) approved successfully." });
    }

    /// <summary>
    /// Reject a request at the current level
    /// </summary>
    [HttpPost("reject")]
    public async Task<IActionResult> Reject([FromBody] ApprovalActionDto dto)
    {
        await _approvalService.RejectAsync(dto.RequestId, dto.LevelNo, dto.ApproverUserId, dto.Comment, dto.IpAddress);
        return Ok(new { Message = "Request rejected successfully." });
    }


    [HttpPost("BulkReject")]
    public async Task<IActionResult> BulkReject([FromBody] List<ApprovalActionDto> approvals)
    {
        if (approvals == null || approvals.Count == 0)
            return BadRequest("No approvals provided.");

        foreach (var approval in approvals)
        {
            await _approvalService.RejectAsync(approval.RequestId, approval.LevelNo, approval.ApproverUserId, approval.Comment, approval.IpAddress);
        }

        return Ok(new { Message = $"{approvals.Count} request(s) rejected successfully." });
    }


    [HttpPost("ApproveRequestAsync")]
    public async Task<IActionResult> ApproveRequestAsync(ApprovalActionDto approvalActionDto)
    {
        var request = await _context.ApprovalRequests.FirstOrDefaultAsync(p => p.RequestId == approvalActionDto.RequestId);
        if (request == null) return NotFound("Request not found");

        // Find workflow level(s) where this user is an approver
        var approverLevels = await (
                from a in _context.ApprovalApprovers
                join w in _context.ApprovalWorkflows on a.WorkflowId equals w.WorkflowId
                where a.UserId == approvalActionDto.ApproverUserId && w.RequestType == request.RequestType
                select w.LevelNo
            ).ToListAsync();

        if (!approverLevels.Any())
            return Forbid("You are not configured as an approver for this request type.");

        int approverLevel = approverLevels.Min(); // e.g. Level 2 approver

        // ✅ Allow approving current or lower levels
        if (approverLevel >= request.CurrentLevelNo)
        {
            // Mark any unapproved lower levels as "ApprovedByHigher"
            var pendingLowerActions = await _context.ApprovalActions
                .Where(a => a.RequestId == approvalActionDto.RequestId &&
                            a.LevelNo < approverLevel)
                .ToListAsync();

            //var pendingLowerActions = await _context.ApprovalActions
            //    .Where(a => a.RequestId == requestId &&
            //                a.LevelNo < approverLevel &&
            //                a.Action == null)
            //    .ToListAsync();

            foreach (var lowerAction in pendingLowerActions)
            {
                lowerAction.ActionStatus = "APPROVED";
                lowerAction.ActionComment = "Auto-approved by higher-level approver.";
                lowerAction.ActionDate = DateTime.UtcNow;
                //lowerAction.ActedOnLowerLevel = true;
            }

            // Record current approver’s action
            var approvalAction = new ApprovalAction
            {
                RequestId = approvalActionDto.RequestId,
                LevelNo = approverLevel,
                ApproverId = approvalActionDto.ApproverUserId,
                ActionStatus = approvalActionDto.Action.ToUpper(), // APPROVED or REJECTED
                ActionComment = approvalActionDto.Comment,
                ActionDate = DateTime.UtcNow,
                IpAddress = approvalActionDto.IpAddress
                //ActedOnLowerLevel = (request.CurrentLevelNo < approverLevel)
            };

            _context.ApprovalActions.Add(approvalAction);

            // Update request status
            if (approvalActionDto.Action.Equals("REJECTED", StringComparison.OrdinalIgnoreCase))
            {
                request.Status = "REJECTED";
            }
            else
            {
                // If last level → finalize
                var maxLevel = await _context.ApprovalWorkflows
                    .Where(w => w.RequestType == request.RequestType)
                    .MaxAsync(w => w.LevelNo);

                request.CurrentLevelNo = approverLevel + 1;
                request.Status = request.CurrentLevelNo > maxLevel ? "APPROVED" : "PENDING";
            }

            await _context.SaveChangesAsync();
            return Ok("Request processed successfully.");
        }

        return Forbid("You cannot approve higher-level requests.");
    }

    [HttpPost("BulkApproveRequestsAsync")]
    public async Task<IActionResult> BulkApproveRequestsAsync(List<ApprovalActionDto> approvalActions)
    {
        if (approvalActions == null || !approvalActions.Any())
            return BadRequest("No approval actions provided.");

        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var requestIds = approvalActions.Select(a => a.RequestId).Distinct().ToList();

            var requests = await _context.ApprovalRequests
                .Where(r => requestIds.Contains(r.RequestId))
                .ToListAsync();

            if (!requests.Any())
                return NotFound("No valid approval requests found.");

            // Load all approver levels for all users in the bulk batch
            var approverUserIds = approvalActions.Select(a => a.ApproverUserId).Distinct().ToList();

            var approverLevels = await (
                from a in _context.ApprovalApprovers
                join w in _context.ApprovalWorkflows on a.WorkflowId equals w.WorkflowId
                where approverUserIds.Contains(a.UserId)
                select new { a.UserId, w.RequestType, w.LevelNo }
            ).ToListAsync();

            var newActions = new List<ApprovalAction>();
            var updatedRequests = new List<ApprovalRequest>();
            var updatedLowerActions = new List<ApprovalAction>();

            foreach (var dto in approvalActions)
            {
                var request = requests.FirstOrDefault(r => r.RequestId == dto.RequestId);
                if (request == null) continue;

                // Determine approver’s level for this request type
                var approverLevel = approverLevels
                    .Where(l => l.UserId == dto.ApproverUserId && l.RequestType == request.RequestType)
                    .Select(l => l.LevelNo)
                    .DefaultIfEmpty(int.MaxValue)
                    .Min();

                if (approverLevel == int.MaxValue)
                    continue; // Skip if user not an approver for this request type

                // ✅ Allow higher-level approvers to act on current or lower levels
                if (approverLevel >= request.CurrentLevelNo)
                {
                    // 1️⃣ Auto-approve lower-level pending actions
                    var lowerActions = await _context.ApprovalActions
                        .Where(a => a.RequestId == request.RequestId && a.LevelNo < approverLevel)
                        .ToListAsync();

                    foreach (var la in lowerActions)
                    {
                        la.ActionStatus = "APPROVED";
                        la.ActionComment = "Auto-approved by higher-level approver.";
                        la.ActionDate = DateTime.UtcNow;
                        updatedLowerActions.Add(la);
                    }

                    // 2️⃣ Add new approval action
                    newActions.Add(new ApprovalAction
                    {
                        RequestId = dto.RequestId,
                        LevelNo = approverLevel,
                        ApproverId = dto.ApproverUserId,
                        ActionStatus = dto.Action.ToUpper(),
                        ActionComment = dto.Comment,
                        ActionDate = DateTime.UtcNow,
                        IpAddress = dto.IpAddress
                    });

                    // 3️⃣ Update request status
                    if (dto.Action.Equals("REJECTED", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Status = "REJECTED";
                    }
                    else
                    {
                        var maxLevel = await _context.ApprovalWorkflows
                            .Where(w => w.RequestType == request.RequestType)
                            .MaxAsync(w => w.LevelNo);

                        request.CurrentLevelNo = approverLevel + 1;
                        request.Status = request.CurrentLevelNo > maxLevel ? "APPROVED" : "PENDING";
                    }

                    updatedRequests.Add(request);
                }
            }

            // 4️⃣ Perform bulk operations for performance
            if (updatedLowerActions.Any())
                await _context.BulkUpdateAsync(updatedLowerActions);

            if (newActions.Any())
                await _context.BulkInsertAsync(newActions);

            if (updatedRequests.Any())
                await _context.BulkUpdateAsync(updatedRequests);

            await transaction.CommitAsync();
            if (newActions.Any())
            {
                string allowRedirect = "false", redirectEmail = string.Empty, emailNotification = "false";

                var configs = _context.ConfigValues.ToList();
                if (configs != null && configs.Count > 0)
                {
                    var allowRedirectConfig = configs.FirstOrDefault(c => c.Name == "ALLOW_EMAIL_REDIRECT");
                    if (allowRedirectConfig != null)
                    {
                        allowRedirect = allowRedirectConfig.Value;
                    }
                    var redirectEmailConfig = configs.FirstOrDefault(c => c.Name == "REDIRECT_EMAIL_TO");
                    if (redirectEmailConfig != null)
                    {
                        redirectEmail = redirectEmailConfig.Value;
                    }
                    var emailNotificationConfig = configs.FirstOrDefault(c => c.Name == "EMAIL_NOTIFICATION");
                    if (emailNotificationConfig != null)
                    {
                        emailNotification = emailNotificationConfig.Value;
                    }
                }


                //var requestIds = new List<int> { 2578 }; // your array

                var query =
                    from t in _context.Timesheets
                    join ar in _context.ApprovalRequests
                        on t.TimesheetId equals ar.TimesheetId
                    join pr in _context.Projects
                        on t.ProjectId equals pr.ProjectId
                    where requestIds.Contains(ar.RequestId)
                    select new
                    {
                        Timesheet = t,
                        RequestId = ar.RequestId,
                        ApprovalStatus = ar.Status,

                        Comment = _context.ApprovalActions
                                    .Where(a => a.RequestId == ar.RequestId)
                                    .OrderBy(a => a.ActionId)
                                    .Select(a => a.ActionComment)
                                    .FirstOrDefault(),

                        IPAddress = _context.ApprovalActions
                                    .Where(a => a.RequestId == ar.RequestId)
                                    .OrderBy(a => a.ActionId)
                                    .Select(a => a.IpAddress)
                                    .FirstOrDefault(),

                        ApproverId = pr.ProjectManagerId,
                        ApproverName = pr.ProjectManagerName
                    };

                var result = await query
                    .OrderBy(x => x.Timesheet.TimesheetDate ?? DateOnly.MaxValue)
                    .ToListAsync();



                //var approvedRequests1 = await _context.ApprovalRequests
                //        .Where(r => requestIds.Contains(r.RequestId))
                //        .Include(r => r.Timesheet)
                //            .ThenInclude(t => t.Employee)
                //        .ToListAsync();

                //// Example usage:
                //foreach (var a in approvedRequests1)
                //{
                //    var empName = a.Timesheet.Employee.DisplayedName;
                //}
                List<LevelDetailsDto> users = new List<LevelDetailsDto>();
                List<LevelDetailsDto> StatusUpdateNotificationUsers = new List<LevelDetailsDto>();

                var statusUsers = (_config?["StatusUpdateNotificationEmail"] ?? string.Empty)
                     .Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(e => e.Trim())
                     .ToList();


                if (statusUsers != null && statusUsers.Count() > 0)
                    StatusUpdateNotificationUsers = _context.Users.Where(u => statusUsers.Contains(u.Username)).Select(u => new LevelDetailsDto
                    {
                        UserId = u.UserId,
                        Username = u.Username,
                        FullName = u.FullName,
                        Email = u.Email
                    }).ToList();


                var ApprovedRequests = await _context.ApprovalRequests
                .Where(r => newActions.Select(p => p.RequestId).Contains(r.RequestId)).Include(t => t.Actions).Include(t => t.Timesheet).ThenInclude(t => t.Employee)
                .ToListAsync();
                var maxLevel = await _context.ApprovalWorkflows
                                    .Where(w => w.RequestType == ApprovedRequests.FirstOrDefault().RequestType)
                                    .MaxAsync(w => w.LevelNo);
                var Status = ApprovedRequests.FirstOrDefault().Status;
                var currentLevel = ApprovedRequests.FirstOrDefault()?.CurrentLevelNo ?? 0;

                foreach (var req in ApprovedRequests)
                {
                    req.Timesheet.Comment = req.Actions.OrderBy(a => a.ActionId)
                                    .Select(a => a.ActionComment)
                                    .FirstOrDefault();
                    if (!string.IsNullOrEmpty(emailNotification) && emailNotification.ToLower() == "true")
                    {
                        User importerUser = new User();
                        if (req.Status.ToUpper() == "REJECTED")
                        {
                            var AdminUsers = await _context.Users
                                    .Where(u => u.Username == req.Timesheet.CreatedBy)
                                    .Select(u => new LevelDetailsDto
                                    {
                                        UserId = u.UserId,
                                        Username = u.Username,
                                        FullName = u.FullName,
                                        Email = u.Email
                                    }).ToListAsync();

                            users.AddRange(AdminUsers);
                            //users.AddRange(StatusUpdateNotificationUsers);

                        }
                        else
                        {
                            if (req.CurrentLevelNo < maxLevel)
                            {
                                users.AddRange((await _helper.GetUsersByLevel(req.CurrentLevelNo)).Where(p => result.Select(q => q.ApproverId).Contains(p.Username)).ToList());
                            }
                            else
                            {
                                users.AddRange(await _helper.GetUsersByLevel(req.CurrentLevelNo));
                                //users.AddRange(StatusUpdateNotificationUsers);


                            }
                        }
                    }

                }


                List<Timesheet> timesheetList = ApprovedRequests.Select(p => p.Timesheet).ToList();

                foreach (var user in users.Distinct())
                {
                    var email = user.Email;

                    if (Status.ToUpper() != "REJECTED")
                    {
                        if (allowRedirect.ToLower() == "true" && !string.IsNullOrEmpty(redirectEmail))
                        {
                            await _taskQueue.QueueBackgroundWorkItemAsync(async (sp, ct) =>
                            {
                                await _emailService.SendEmailWithRedirectAsync(email, timesheetList, new User() { Username = user.Username, FullName = user.FullName, Projects = new List<Project>() }, redirectEmail);
                            });
                        }
                        else
                        {
                            await _taskQueue.QueueBackgroundWorkItemAsync(async (sp, ct) =>
                            {
                                await _emailService.SendEmailAsync(email, timesheetList, new User() { Username = user.Username, FullName = user.FullName, Projects = new List<Project>() });
                            });
                        }
                    }
                    else
                    {
                        await _taskQueue.QueueBackgroundWorkItemAsync(async (sp, ct) =>
                        {
                            await _emailService.SendTimesheetRejectedEmailAsync(email, timesheetList, new User() { Username = user.Username, FullName = user.FullName, Projects = new List<Project>() }, Status.ToUpper());

                            foreach (var StatusUpdateNotificationUser in StatusUpdateNotificationUsers.Distinct())
                            {
                                await _emailService.SendTimesheetRejectedEmailAsync(StatusUpdateNotificationUser.Email, timesheetList, new User() { Username = StatusUpdateNotificationUser.Username, FullName = StatusUpdateNotificationUser.FullName, Projects = new List<Project>() }, Status.ToUpper());
                            }
                        });
                        break;
                    }

                }


                if (currentLevel > maxLevel)
                    foreach (var StatusUpdateNotificationUser in StatusUpdateNotificationUsers.Distinct())
                    {
                        await _emailService.SendTimesheetRejectedEmailAsync(StatusUpdateNotificationUser.Email, timesheetList, new User() { Username = StatusUpdateNotificationUser.Username, FullName = StatusUpdateNotificationUser.FullName, Projects = new List<Project>() }, Status.ToUpper());
                    }
            }
            return Ok(new
            {
                Message = "Bulk approval actions processed successfully.",
                ProcessedCount = updatedRequests.Count,
                Approved = updatedRequests.Count(r => r.Status == "APPROVED"),
                Pending = updatedRequests.Count(r => r.Status == "PENDING"),
                Rejected = updatedRequests.Count(r => r.Status == "REJECTED")
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, $"Error processing bulk approvals: {ex.Message}");
        }
    }


}
