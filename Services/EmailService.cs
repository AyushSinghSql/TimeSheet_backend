using Microsoft.Extensions.Options;
using NetTopologySuite.Index.HPRtree;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using TimeSheet.DTOs;
using TimeSheet.Models;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace TimeSheet.Services
{
    public class SmtpSettings
    {
        public string Server { get; set; }
        public int Port { get; set; }
        public string FromEmail { get; set; }
        public string Password { get; set; }
        public string AppUrl { get; set; }

    }

    public class EmailService
    {

        private readonly SmtpSettings _smtpSettings;

        public EmailService(IOptions<SmtpSettings> smtpOptions)
        {
            _smtpSettings = smtpOptions.Value;
        }
        //private readonly string _smtpServer = "smtp.gmail.com"; // or smtp.office365.com, smtp.yourdomain.com
        //private readonly int _port = 587; // 587 (TLS), 465 (SSL), 25 (default)
        //private readonly string _fromEmail = "k44498426@gmail.com";
        //private readonly string _password = "mtif tixl legc rirg"; // ⚠️ Use secrets manager in real apps

        //private readonly string _smtpServer = "smtp.office365.com";
        //private readonly int _port = 587; // TLS port
        //private readonly string _fromEmail = "Demo@revolvespl.com";
        //private readonly string _password = "Welcome2Rev!!"; // Use App Password or Entra ID client credential

        public async Task SendEmailAsync(string toEmail, List<Timesheet> pendingTimesheets, User user)
        {
            try
            {
                using (var client = new SmtpClient(_smtpSettings.Server, Convert.ToInt32(_smtpSettings.Port)))
                {
                    client.EnableSsl = true;
                    client.Credentials = new NetworkCredential(_smtpSettings.FromEmail, _smtpSettings.Password);
                    //string proj_id = pendingTimesheets.FirstOrDefault()?.ProjectId;
                    string subject = "Request for Timesheet Approval";
                    //    string body = $@"
                    //    <p>Hello {user.FullName},</p>
                    //    <p>Please check Timesheets to approve for Project {proj_id} at the link below:</p>
                    //    <p>
                    //        <a href='https://timesheet-hw8n.vercel.app/login?userid={user.Username}'>
                    //            View Timesheets to Approve
                    //        </a>
                    //    </p>
                    //    <br/>
                    //    <p>Regards,<br/>Project Team</p>
                    //";

                    string approvalLink = "https://timesheet-hw8n.vercel.app/login?userid=" + user.Username;

                    approvalLink = _smtpSettings.AppUrl + user.Username;
                    string body = $@"
                            <p>Hello <b>{user.FullName}</b>,</p>

                            <p>You have timesheets pending approval for Project(s).<br/>
                            {string.Join("", user.Projects.Select(t => $@"
                                   <b>{t.ProjectId} - ({t.ProjectName})</b> <br/>"))}


                                
                            Click the button below to review the pending timesheets:</p>

                            <!-- Approval Button -->
                            <p>
                                <a href='{approvalLink}'
                                   style='background-color:#007bff; color:#fff; padding:10px 15px; 
                                          text-decoration:none; border-radius:5px; font-weight:bold;'>
                                   View Timesheets to Approve
                                </a>
                            </p>

                            <!-- Summary Table -->
                            <p>Below is a summary of the pending timesheets dated <b> {pendingTimesheets.FirstOrDefault()?.TimesheetDate.GetValueOrDefault().ToString("MM/dd/yyyy")} </b>, for the employees listed below.</p>
                            <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse; font-family:Arial, sans-serif; font-size:13px;'>
                                <tr style='background-color:#f2f2f2;'>
                                    <th>Batch Id</th>
                                    <th>Employee Id</th>
                                    <th>Employee Name</th>
                                </tr>
                                {string.Join("", pendingTimesheets.Select(p => new { p.Employee?.EmployeeId, p.Employee?.DisplayedName, p.BatchId }).Distinct().Select(t => $@"
                                    <tr>
                                        <td>{t.BatchId}</td>                                        
                                        <td>{t.EmployeeId}</td>
                                        <td>{t.DisplayedName}</td>
                                    </tr>"))}
                            </table>

                            <p>If the button above doesn’t work, you can copy and paste this link into your browser:<br/>
                            <a href='{approvalLink}'>{approvalLink}</a></p>

                            <p>Thank you for your prompt attention.</p>

                            <p>Best regards,<br/>Project Team</p>
                            ";


                    var mailMessage = new MailMessage
                    {
                        From = new MailAddress(_smtpSettings.FromEmail),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true
                    };

                    mailMessage.To.Add(toEmail);

                    await client.SendMailAsync(mailMessage);
                }
            }
            catch (Exception ex)
            {
                // Log or handle the exception as needed
                Console.WriteLine($"Error sending email: {ex.Message}");
            }
        }

        public async Task SendEmailDTOAsync(string toEmail, List<TimesheetNotifyDTO> pendingTimesheets, User user)
        {
            try
            {
                using (var client = new SmtpClient(_smtpSettings.Server, Convert.ToInt32(_smtpSettings.Port)))
                {
                    client.EnableSsl = true;
                    client.Credentials = new NetworkCredential(_smtpSettings.FromEmail, _smtpSettings.Password);
                    //string proj_id = pendingTimesheets.FirstOrDefault()?.ProjectId;
                    string subject = "Request for Timesheet Approval";
                    //    string body = $@"
                    //    <p>Hello {user.FullName},</p>
                    //    <p>Please check Timesheets to approve for Project {proj_id} at the link below:</p>
                    //    <p>
                    //        <a href='https://timesheet-hw8n.vercel.app/login?userid={user.Username}'>
                    //            View Timesheets to Approve
                    //        </a>
                    //    </p>
                    //    <br/>
                    //    <p>Regards,<br/>Project Team</p>
                    //";

                    string approvalLink = "https://timesheet-hw8n.vercel.app/login?userid=" + user.Username;

                    approvalLink = _smtpSettings.AppUrl + user.Username;
                    string body = $@"
                            <p>Hello <b>{user.FullName}</b>,</p>

                            <p>Click the button below to review the pending timesheets:</p>

                            <!-- Approval Button -->
                            <p>
                                <a href='{approvalLink}'
                                   style='background-color:#007bff; color:#fff; padding:10px 15px; 
                                          text-decoration:none; border-radius:5px; font-weight:bold;'>
                                   View Timesheets to Approve
                                </a>
                            </p>

                            <!-- Summary Table -->
                            <p>Below is a summary of the pending timesheets for the employees listed below.</p>
                            <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse; font-family:Arial, sans-serif; font-size:13px;'>
                                <tr style='background-color:#f2f2f2;'>
                                    <th>Employee Id</th>
                                    <th>Employee Name</th>
                                </tr>
                                {string.Join("", pendingTimesheets.Select(p => new { p.EmployeeId, p.DisplayedName }).Distinct().Select(t => $@"
                                    <tr>
                                        <td>{t.EmployeeId}</td>
                                        <td>{t.DisplayedName}</td>
                                    </tr>"))}
                            </table>

                            <p>If the button above doesn’t work, you can copy and paste this link into your browser:<br/>
                            <a href='{approvalLink}'>{approvalLink}</a></p>

                            <p>Thank you for your prompt attention.</p>

                            <p>Best regards,<br/>Project Team</p>
                            ";


                    var mailMessage = new MailMessage
                    {
                        From = new MailAddress(_smtpSettings.FromEmail),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true
                    };

                    mailMessage.To.Add(toEmail);

                    await client.SendMailAsync(mailMessage);
                }
            }
            catch (Exception ex)
            {
                // Log or handle the exception as needed
                Console.WriteLine($"Error sending email: {ex.Message}");
            }
        }

        public async Task SendEmailWithRedirectAsync(string toEmail, List<Timesheet> pendingTimesheets, User user, string redirectEmail, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var sw = Stopwatch.StartNew();

                Console.WriteLine("Creating SMTP client...");
                using (var client = new SmtpClient(_smtpSettings.Server, Convert.ToInt32(_smtpSettings.Port)))
                {
                    Console.WriteLine($"Client created: {sw.ElapsedMilliseconds}ms");
                    client.EnableSsl = true;
                    Console.WriteLine("Setting credentials...");
                    client.Credentials = new NetworkCredential(_smtpSettings.FromEmail, _smtpSettings.Password);
                    //string proj_id = pendingTimesheets.FirstOrDefault()?.ProjectId;
                    string subject = "Request for Timesheet Approval";
                    //    string body = $@"
                    //    <p>Hello {user.FullName},</p>
                    //    <p>Please check Timesheets to approve for Project {proj_id} at the link below:</p>
                    //    <p>
                    //        <a href='https://timesheet-hw8n.vercel.app/login?userid={user.Username}'>
                    //            View Timesheets to Approve
                    //        </a>
                    //    </p>
                    //    <br/>
                    //    <p>Regards,<br/>Project Team</p>
                    //";

                    string approvalLink = "https://timesheet-hw8n.vercel.app/login?userid=" + user.Username;
                    approvalLink = _smtpSettings.AppUrl + user.Username;

                    string body = $@"
                                <p><b>Original Email Sent:<br/>                      
                                To:</b> <a href='mailto:{toEmail}'>{toEmail}</a>,</p>
                                <p>Hello <b>{user.FullName}</b>,</p>
                                <p>You have timesheets pending approval for Project(s).<br/>
                                {string.Join("", user.Projects.Select(t => $@"
                                    <b>{t.ProjectId} - ({t.ProjectName})</b><br/>"))}

                                Click the button below to review the pending timesheets:</p>

                                <!-- Approval Button -->
                                <p>
                                    <a href='{approvalLink}'
                                        style='background-color:#007bff; color:#fff; padding:10px 15px; 
                                                text-decoration:none; border-radius:5px; font-weight:bold;'>
                                        View Timesheets to Approve
                                    </a>
                                </p>

                                <!-- Summary Table -->
                                <p>Below is a summary of the pending timesheets dated 
                                    <b>{pendingTimesheets.FirstOrDefault()?.TimesheetDate.GetValueOrDefault().ToString("MM/dd/yyyy")}</b>, 
                                    for the employees listed below.</p>

                                <table border='1' cellpadding='6' cellspacing='0' 
                                        style='border-collapse:collapse; font-family:Arial, sans-serif; font-size:13px;'>
                                    <tr style='background-color:#f2f2f2;'>
                                        <th>Batch Id</th>
                                        <th>Employee Id</th>
                                        <th>Employee Name</th>
                                    </tr>
                                    {string.Join("", pendingTimesheets
                                                            .Select(p => new { p.BatchId, p.Employee?.EmployeeId, p.Employee?.DisplayedName })
                                                            .Distinct()
                                                            .Select(t => $@"
                                            <tr>
                                                <td>{t.BatchId}
                                                <td>{t.EmployeeId}</td>
                                                <td>{t.DisplayedName}</td>
                                            </tr>"))}
                                </table>

                                <p>If the button above doesn’t work, you can copy and paste this link into your browser:<br/>
                                <a href='{approvalLink}'>{approvalLink}</a></p>

                                <p>Thank you for your prompt attention.</p>

                                <p>Best regards,<br/>Project Team</p>";


                    //string body = $@"
                    //        <p><b>Orignal Email Send:</p><br/>                           
                    //        <p>To: {redirectEmail},</p><b><br/>
                    //        <p>Hello <b>{user.FullName}</b>,</p>
                    //        <p>You have timesheets pending approval for Project(s).<br/>
                    //        {string.Join("", user.Projects.Select(t => $@"
                    //               <b>{t.ProjectId} - ({t.ProjectName})</b> <br/>"))}



                    //        Please review them at the link below:</p>

                    //        <!-- Approval Button -->
                    //        <p>
                    //            <a href='{approvalLink}'
                    //               style='background-color:#007bff; color:#fff; padding:10px 15px; 
                    //                      text-decoration:none; border-radius:5px; font-weight:bold;'>
                    //               View Timesheets to Approve
                    //            </a>
                    //        </p>

                    //        <!-- Summary Table -->
                    //        <p>Below is a summary of the pending timesheets dated <b> {pendingTimesheets.FirstOrDefault()?.TimesheetDate.GetValueOrDefault().ToString("MM/dd/yyyy")} </b>, for the employees listed below.</p>
                    //        <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse; font-family:Arial, sans-serif; font-size:13px;'>
                    //            <tr style='background-color:#f2f2f2;'>
                    //                <th>Employee Id</th>
                    //                <th>Employee Name</th>
                    //            </tr>
                    //            {string.Join("", pendingTimesheets.Select(p => new { p.EmployeeId, p.DisplayedName }).Distinct().Select(t => $@"
                    //                <tr>
                    //                    <td>{t.EmployeeId}</td>
                    //                    <td>{t.DisplayedName}</td>
                    //                </tr>"))}
                    //        </table>

                    //        <p>If the button above doesn’t work, you can copy and paste this link into your browser:<br/>
                    //        <a href='{approvalLink}'>{approvalLink}</a></p>

                    //        <p>Thank you for your prompt attention.</p>

                    //        <p>Best regards,<br/>Project Team</p>
                    //        ";

                    Console.WriteLine($"Configuring Mail Message...");
                    var mailMessage = new MailMessage
                    {
                        From = new MailAddress(_smtpSettings.FromEmail),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true
                    };

                    mailMessage.To.Add(redirectEmail);
                    Console.WriteLine($"Connecting & sending...");

                    await client.SendMailAsync(mailMessage);
                    Console.WriteLine($"Sent! Total time: {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                // Log or handle the exception as needed
                Console.WriteLine($"Error sending email: {ex.Message}");
            }
        }
        public async Task SendEmailWithRedirectDTOAsync(string toEmail, List<TimesheetNotifyDTO> pendingTimesheets, User user, string redirectEmail, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var sw = Stopwatch.StartNew();

                Console.WriteLine("Creating SMTP client...");
                using (var client = new SmtpClient(_smtpSettings.Server, Convert.ToInt32(_smtpSettings.Port)))
                {
                    Console.WriteLine($"Client created: {sw.ElapsedMilliseconds}ms");
                    client.EnableSsl = true;
                    Console.WriteLine("Setting credentials...");
                    client.Credentials = new NetworkCredential(_smtpSettings.FromEmail, _smtpSettings.Password);
                    //string proj_id = pendingTimesheets.FirstOrDefault()?.ProjectId;
                    string subject = "Request for Timesheet Approval";
                    //    string body = $@"
                    //    <p>Hello {user.FullName},</p>
                    //    <p>Please check Timesheets to approve for Project {proj_id} at the link below:</p>
                    //    <p>
                    //        <a href='https://timesheet-hw8n.vercel.app/login?userid={user.Username}'>
                    //            View Timesheets to Approve
                    //        </a>
                    //    </p>
                    //    <br/>
                    //    <p>Regards,<br/>Project Team</p>
                    //";

                    string approvalLink = "https://timesheet-hw8n.vercel.app/login?userid=" + user.Username;
                    approvalLink = _smtpSettings.AppUrl + user.Username;

                    string body = $@"
                                <p><b>Original Email Sent:<br/>                      
                                To:</b> <a href='mailto:{toEmail}'>{toEmail}</a>,</p>
                                <p>Hello <b>{user.FullName}</b>,</p>
                                <p>Click the button below to review the pending timesheets:</p>

                                <!-- Approval Button -->
                                <p>
                                    <a href='{approvalLink}'
                                        style='background-color:#007bff; color:#fff; padding:10px 15px; 
                                                text-decoration:none; border-radius:5px; font-weight:bold;'>
                                        View Timesheets to Approve
                                    </a>
                                </p>

                                <!-- Summary Table -->
                                <p>Below is a summary of the pending timesheets for the employees listed below.</p>

                                <table border='1' cellpadding='6' cellspacing='0' 
                                        style='border-collapse:collapse; font-family:Arial, sans-serif; font-size:13px;'>
                                    <tr style='background-color:#f2f2f2;'>
                                        <th>Batch Id</th>
                                        <th>Employee Id</th>
                                        <th>Employee Name</th>
                                    </tr>
                                    {string.Join("", pendingTimesheets
                                                            .Select(p => new { p.BatchId, p.EmployeeId, p.DisplayedName })
                                                            .Distinct()
                                                            .Select(t => $@"
                                            <tr>
                                                <td>{t.EmployeeId}</td>
                                                <td>{t.DisplayedName}</td>
                                            </tr>"))}
                                </table>

                                <p>If the button above doesn’t work, you can copy and paste this link into your browser:<br/>
                                <a href='{approvalLink}'>{approvalLink}</a></p>

                                <p>Thank you for your prompt attention.</p>

                                <p>Best regards,<br/>Project Team</p>";

                    Console.WriteLine($"Configuring Mail Message...");
                    var mailMessage = new MailMessage
                    {
                        From = new MailAddress(_smtpSettings.FromEmail),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true
                    };

                    mailMessage.To.Add(redirectEmail);
                    Console.WriteLine($"Connecting & sending...");

                    await client.SendMailAsync(mailMessage);
                    Console.WriteLine($"Sent! Total time: {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                // Log or handle the exception as needed
                Console.WriteLine($"Error sending email: {ex.Message}");
            }
        }

        public async Task SendTimesheetRejectedEmailAsync(string toEmail, List<Timesheet> timesheets, User user, string status)
        {
            try
            {
                using (var client = new SmtpClient(_smtpSettings.Server, Convert.ToInt32(_smtpSettings.Port)))
                {
                    client.EnableSsl = true;
                    client.Credentials = new NetworkCredential(_smtpSettings.FromEmail, _smtpSettings.Password);

                    string timesheetDate = timesheets.FirstOrDefault()?.TimesheetDate.GetValueOrDefault().ToString("MM/dd/yyyy") ?? "";
                    string actionLink = "https://timesheet-hw8n.vercel.app/login?userid=" + user.Username;
                    actionLink = _smtpSettings.AppUrl + user.Username;

                    string subject = status.ToLower() switch
                    {
                        "rejected" => "Timesheet Rejection Notification",
                        "approved" => "Timesheet Approval Confirmation",
                        _ => "Request for Timesheet Approval"
                    };

                    string actionColor = status.ToLower() switch
                    {
                        "rejected" => "#dc3545", // Red
                        "approved" => "#28a745", // Green
                        _ => "#007bff"           // Blue (Pending/Request)
                    };

                    string actionText = status.ToLower() switch
                    {
                        "rejected" => "View Rejected Timesheets",
                        "approved" => "View Approved Timesheets",
                        _ => "View Timesheets to Approve"
                    };

                    string introMessage = status.ToLower() switch
                    {
                        "rejected" => "The following timesheet(s) have been <b style='color:red;'>rejected</b> and require your attention.",
                        "approved" => "The following timesheet(s) have been <b style='color:green;'>approved</b> successfully.",
                        _ => "You have pending timesheets awaiting your approval."
                    };

                    string body = $@"
                <p>Hello <b>{user.FullName}</b>,</p>

                <p>{introMessage}</p>

                <p>Click the button below to review the {status} timesheets:</p>

                <!-- Action Button -->
                <p>
                    <a href='{actionLink}'
                       style='background-color:{actionColor}; color:#fff; padding:10px 15px; 
                              text-decoration:none; border-radius:5px; font-weight:bold;'>
                       {actionText}
                    </a>
                </p>

                <!-- Summary Table -->
                <p>Below is a summary of the timesheets dated <b>{timesheetDate}</b>:</p>

                <table border='1' cellpadding='6' cellspacing='0' 
                       style='border-collapse:collapse; font-family:Arial, sans-serif; font-size:13px; width:100%; max-width:600px;'>
                    <tr style='background-color:#f2f2f2;'>
                        <th>Batch Id</th>                
                        <th>Employee Id</th>
                        <th>Employee Name</th>
                        {(status.ToLower() == "rejected" ? "<th>Remarks</th>" : "")}
                    </tr>
                    {string.Join("", timesheets.Select(t => $@"
                        <tr>
                            <td>{t.BatchId}</td>                
                            <td>{t.Employee.EmployeeId}</td>
                            <td>{t.Employee.DisplayedName}</td>
                            {(status.ToLower() == "rejected" ? $"<td>{(string.IsNullOrWhiteSpace(t.Comment) ? "—" : t.Comment)}</td>" : "")}
                        </tr>"))}
                </table>

                        <p>If the button above doesn’t work, you can copy and paste this link into your browser:<br/>
                            <a href='{actionLink}'>{actionLink}</a></p>

                            <p>Thank you for your prompt attention.</p>
                <p>Best regards,<br/><b>Project Team</b></p>
            ";

                    var mailMessage = new MailMessage
                    {
                        From = new MailAddress(_smtpSettings.FromEmail),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true
                    };

                    mailMessage.To.Add(toEmail);
                    await client.SendMailAsync(mailMessage);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending email: {ex.Message}");
            }
        }

        public async Task SendDailySummaryEmailAsync(string toEmail, DailySummaryResponse summary)
        {
            try
            {
                using (var client = new SmtpClient(_smtpSettings.Server, Convert.ToInt32(_smtpSettings.Port)))
                {
                    client.EnableSsl = true;
                    client.Credentials = new NetworkCredential(_smtpSettings.FromEmail, _smtpSettings.Password);


                    ////////////////////////////////////////////////////////////////////////////////////////
                    foreach (var batch in summary.ApprovedByApprover.Select(b => b.BatchId).Distinct())
                    {

                        string subject = $"Daily Timesheet Summary for Batch ({batch}) – {summary.Date}";

                        // ---- Build HTML Body ----
                        string approvedRows = string.Join("", summary.ApprovedByApprover.Where(a => a.BatchId == batch).FirstOrDefault().approvalSummaries
                            .Select(a => $@"
                    <tr>
                        <td>{a.Level}</td>
                        <td><ul style='padding-left:16px; margin:0;'><li>{a.ApproverName}</li></ul></td>
                        <td>{a.Count}</td>
                    </tr>
                "));

                        //        string rejectedRows = string.Join("", summary.RejectedByApprover.Where(a => a.BatchId == batch).FirstOrDefault().approvalSummaries
                        //            .Select(a => $@"
                        //    <tr>
                        //        <td>{a.Level}</td>
                        //        <td><ul style='padding-left:16px; margin:0;'><li>{a.ApproverName}</li></ul></td>
                        //        <td>{a.Count}</td>
                        //    </tr>
                        //"));
                        string rejectedRows =
                                string.Join("",
                                    (summary.RejectedByApprover?
                                        .FirstOrDefault(x => x.BatchId == batch)?
                                        .approvalSummaries ?? new List<ApprovalSummary>())
                                    .Select(a => $@"
                                <tr>
                                    <td>{a.Level}</td>
                                    <td><ul style='padding-left:16px; margin:0;'><li>{a.ApproverName}</li></ul></td>
                                    <td>{a.Count}</td>
                                </tr>
                            ")
                                );


                        string pendingRows = string.Join("", summary.PendingByApprover.Select(p => $@"
                <tr>
                    <td>{p.Level}</td>
                    <td>
                        <ul style='padding-left:16px; margin:0;'>
                            {string.Join("", p.Approvers.Select(ap =>
                                            $"<li>{ap.FullName} ({ap.Username})</li>"
                                        ))}
                        </ul>
                    </td>
                    <td>{p.Count}</td>
                </tr>
            "));

                        string body = $@"
                        <!doctype html>
                        <html>
                        <body style='font-family:Arial, sans-serif; background:#f6f8fb; padding:20px;'>

                        <div style='max-width:800px; margin:auto; background:#fff; border-radius:8px; padding:20px;'>

                            <h2 style='color:#0b3a66; margin-bottom:5px;'>Daily Approval Summary</h2>
                            <p style='color:#555;'>Summary for Batch <b> {batch} </b> -  <b>{summary.Date} </b></p>

                            <!-- Totals -->
                            <div style='display:flex; gap:12px; margin-top:20px;'>
                                <div style='flex:1; background:#eef4fc; padding:12px; border-radius:6px; text-align:center;'>
                                    <div style='font-size:20px; font-weight:bold;'>{summary.ApprovedBatchStatus?.FirstOrDefault(p => p.BatchId == batch)?.Count ?? 0}</div>
                                    <div>Total Approved</div>
                                </div>

                                <div style='flex:1; background:#fffae6; padding:12px; border-radius:6px; text-align:center;'>
                                    <div style='font-size:20px; font-weight:bold;'>{summary.PendingBatchStatus?.FirstOrDefault(p => p.BatchId == batch)?.Count ?? 0}</div>
                                    <div>Total Pending</div>
                                </div>

                                <div style='flex:1; background:#fdeaea; padding:12px; border-radius:6px; text-align:center;'>
                                    <div style='font-size:20px; font-weight:bold;'>{summary.RejectedBatchStatus?
                        .FirstOrDefault(p => p.BatchId == batch)?
                        .Count ?? 0}</div>
                                    <div>Total Rejected</div>
                                </div>
                            </div>

                            <br/>

                            <!-- Approved Table -->
                            <h3 style='color:#0b3a66;'>Approved – By Approver</h3>
                            <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse; width:100%;'>
                                <tr style='background:#f1f6fa;'>
                                    <th>Level</th>
                                    <th>Approver</th>
                                    <th>Count</th>
                                </tr>
                                {approvedRows}
                            </table>

                            <br/>

                            <!-- Rejected Table -->
                            <h3 style='color:#0b3a66;'>Rejected – By Approver</h3>
                            <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse; width:100%;'>
                                <tr style='background:#f1f6fa;'>
                                    <th>Level</th>
                                    <th>Approver</th>
                                    <th>Count</th>
                                </tr>
                                {rejectedRows}
                            </table>

                            <br/>

                            <!-- Pending Table -->
                            <h3 style='color:#0b3a66;'>Pending – By Approval Level</h3>
                            <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse; width:100%;'>
                                <tr style='background:#f1f6fa;'>
                                    <th>Level</th>
                                    <th>Approvers</th>
                                    <th>Count</th>
                                </tr>
                                {pendingRows}
                            </table>

                            <br/>
                            <p style='font-size:12px; color:#777;'>This is an automated summary. Please do not reply.</p>

                        </div>

                        </body>
                        </html>
                        ";

                        var mailMessage = new MailMessage
                        {
                            From = new MailAddress(_smtpSettings.FromEmail),
                            Subject = subject,
                            Body = body,
                            IsBodyHtml = true
                        };

                        mailMessage.To.Add(toEmail);

                        await client.SendMailAsync(mailMessage);
                    }
                    //////////////////////////////////////////////////////////////////////////////////////////////////////////
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending email: {ex.Message}");
            }
        }

        //public async Task SendDailySummaryEmailAsync1(string toEmail, DailySummaryResponse summary)
        //{
        //    try
        //    {
        //        using (var client = new SmtpClient(_smtpSettings.Server, Convert.ToInt32(_smtpSettings.Port)))
        //        {
        //            client.EnableSsl = true;
        //            client.Credentials = new NetworkCredential(_smtpSettings.FromEmail, _smtpSettings.Password);

        //            // ---- LOOP THROUGH EACH BATCH ----
        //            foreach (var batch in summary.ApprovedByApprover.Select(b => b.BatchId).Distinct())
        //            {
        //                // Filter summaries for current batch
        //                var approvedBatch = summary.ApprovedByApprover
        //                    .FirstOrDefault(x => x.BatchId == batch);

        //                var rejectedBatch = summary.RejectedByApprover
        //                    .FirstOrDefault(x => x.BatchId == batch);

        //                var pendingBatch = summary.PendingByApprover
        //                    .Where(p => p.BatchId == batch)
        //                    .ToList();

        //                // ---------- BUILD HTML ROWS PER BATCH ----------
        //                string approvedRows = approvedBatch != null
        //                    ? string.Join("", approvedBatch.approvalSummaries.Select(a => $@"
        //                <tr>
        //                    <td>{a.Level}</td>
        //                    <td>{a.ApproverName}</td>
        //                    <td>{a.Count}</td>
        //                </tr>
        //            "))
        //                    : "";

        //                string rejectedRows = rejectedBatch != null
        //                    ? string.Join("", rejectedBatch.approvalSummaries.Select(a => $@"
        //                <tr>
        //                    <td>{a.Level}</td>
        //                    <td>{a.ApproverName}</td>
        //                    <td>{a.Count}</td>
        //                </tr>
        //            "))
        //                    : "";

        //                string pendingRows = string.Join("", pendingBatch.Select(p => $@"
        //                <tr>
        //                    <td>{p.Level}</td>
        //                    <td>
        //                        <ul style='padding-left:16px; margin:0;'>
        //                            {string.Join("", p.Approvers.Select(ap => $"<li>{ap.FullName} ({ap.Username})</li>"))}
        //                        </ul>
        //                    </td>
        //                    <td>{p.Count}</td>
        //                </tr>
        //            "));

        //                // 🔥 SUBJECT PER BATCH
        //                string subject = $"Daily Timesheet Summary – Batch {batch} – {summary.Date}";

        //                // ---- COMPLETE EMAIL HTML BODY (same as before, just replace rows) ----
        //                string body = $@"<html>... (your same HTML with {approvedRows}, {rejectedRows}, {pendingRows}) ...</html>";

        //                var mailMessage = new MailMessage
        //                {
        //                    From = new MailAddress(_smtpSettings.FromEmail),
        //                    Subject = subject,
        //                    Body = body,
        //                    IsBodyHtml = true
        //                };

        //                //mailMessage.To.Add(toEmail);

        //                //await client.SendMailAsync(mailMessage);
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Error sending email: {ex.Message}");
        //    }
        //}
       
    }
}
