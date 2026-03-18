using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CsvHelper;
using CsvHelper.Configuration;
using EFCore.BulkExtensions;
using ExcelDataReader;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Platforms.Features.DesktopOs.Kerberos;
using Microsoft.VisualBasic;
using NetTopologySuite.Index.HPRtree;
using Npgsql;
using NPOI.SS.Formula.Functions;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Data;
using System.Formats.Asn1;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using TimeSheet;
using TimeSheet.BackgroundQueue;
using TimeSheet.DTOs;
using TimeSheet.Models;
using TimeSheet.Models.YourNamespace.Models;
using TimeSheet.Repository;
using TimeSheet.Services;
using static NPOI.HSSF.UserModel.HeaderFooter;
using static System.Runtime.InteropServices.JavaScript.JSType;

[ApiController]
[Route("api/[controller]")]
public class TimesheetController : ControllerBase
{
    private readonly AppDbContext _context;
    Helper _helper;
    private readonly IUserRepository _repository;
    private string BucketName = "", ExportPath = "";
    private string Region = "";            // Change to your region
    private string AWS_ACCESS_KEY_ID = "c";
    private string AWS_SECRET_ACCESS_KEY = "";
    private int EXPIRES_IN_MINUTES = 0;

    //private static readonly RegionEndpoint bucketRegion = RegionEndpoint.APSouth2;
    private static IAmazonS3 s3Client;
    private readonly IConfiguration _config;
    private readonly EmailService _emailService;
    private readonly IBackgroundTaskQueue _taskQueue;


    public TimesheetController(AppDbContext context, IUserRepository repository, IConfiguration config, EmailService emailService, IBackgroundTaskQueue taskQueue)
    {
        _context = context;
        _helper = new Helper(_context);
        _repository = repository;
        _config = config;
        AWS_ACCESS_KEY_ID = _config["AwsS3:AWS_ACCESS_KEY_ID"];
        AWS_SECRET_ACCESS_KEY = _config["AwsS3:AWS_SECRET_ACCESS_KEY"];
        Region = _config["AwsS3:REGION"];
        BucketName = _config["AwsS3:BUCKETNAME"];
        EXPIRES_IN_MINUTES = Convert.ToInt16(_config["AwsS3:EXPIRES_IN_MINUTES"]);
        ExportPath = _config["ExportPath"];
        _emailService = emailService;
        _taskQueue = taskQueue;
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportTimesheet(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        var timesheets = new List<Timesheet>();
        int imported = 0, skipped = 0;

        using (var stream = file.OpenReadStream())
        {
            IWorkbook workbook = new XSSFWorkbook(stream);
            var sheet = workbook.GetSheetAt(0);

            for (int row = 6; row <= sheet.LastRowNum; row++) // row 0 = header
            {
                var currentRow = sheet.GetRow(row);
                if (currentRow == null) continue;

                var employeeId = currentRow.GetCell(1)?.ToString()?.Trim();
                var dateCell = currentRow.GetCell(0);

                DateOnly? timesheetDate = null;
                if (dateCell != null)
                {
                    if (dateCell.CellType == CellType.Numeric && DateUtil.IsCellDateFormatted(dateCell))
                        timesheetDate = dateCell.DateOnlyCellValue;
                    else if (DateOnly.TryParse(dateCell.ToString(), out var parsed))
                        timesheetDate = parsed;
                }

                // Basic validation
                if (string.IsNullOrEmpty(employeeId) || timesheetDate == null)
                {
                    skipped++;
                    continue;
                }

                // Check for duplicate
                bool exists = _context.Timesheets
                    .Any(t => t.EmployeeId == employeeId && t.TimesheetDate == timesheetDate);

                if (exists)
                {
                    skipped++;
                    continue;
                }

                // Map row to entity
                var ts = new Timesheet
                {
                    TimesheetDate = timesheetDate,
                    EmployeeId = employeeId,
                    TimesheetTypeCode = currentRow.GetCell(2)?.ToString(),
                    WorkingState = currentRow.GetCell(3)?.ToString(),
                    FiscalYear = (int)(currentRow.GetCell(4)?.NumericCellValue ?? 0),
                    Period = (int)(currentRow.GetCell(5)?.NumericCellValue ?? 0),
                    Subperiod = (int?)(currentRow.GetCell(6)?.NumericCellValue),
                    CorrectingRefDate = TryParseDate(currentRow.GetCell(7)),
                    PayType = currentRow.GetCell(8)?.ToString(),
                    GeneralLaborCategory = currentRow.GetCell(9)?.ToString(),
                    TimesheetLineTypeCode = currentRow.GetCell(10)?.ToString(),
                    LaborCostAmount = (decimal?)(currentRow.GetCell(11)?.NumericCellValue),
                    Hours = (decimal?)(currentRow.GetCell(12)?.NumericCellValue),
                    WorkersCompCode = currentRow.GetCell(13)?.ToString(),
                    LaborLocationCode = currentRow.GetCell(14)?.ToString(),
                    OrganizationId = currentRow.GetCell(15)?.ToString(),
                    AccountId = currentRow.GetCell(16)?.ToString(),
                    ProjectId = currentRow.GetCell(17)?.ToString(),
                    ProjectLaborCategory = currentRow.GetCell(18)?.ToString(),
                    ReferenceNumber1 = currentRow.GetCell(19)?.ToString(),
                    ReferenceNumber2 = currentRow.GetCell(20)?.ToString(),
                    OrganizationAbbreviation = currentRow.GetCell(21)?.ToString(),
                    ProjectAbbreviation = currentRow.GetCell(22)?.ToString(),
                    SequenceNumber = (int?)(currentRow.GetCell(23)?.NumericCellValue),
                    EffectiveBillingDate = TryParseDate(currentRow.GetCell(24)),
                    ProjectAccountAbbrev = currentRow.GetCell(25)?.ToString(),
                    MultiStateCode = currentRow.GetCell(26)?.ToString(),
                    ReferenceSequenceNum = (int?)(currentRow.GetCell(27)?.NumericCellValue),
                    TimesheetLineDate = TryParseDate(currentRow.GetCell(28)),
                    Notes = currentRow.GetCell(29)?.ToString(),
                    CreatedBy = "api-import",
                    ModifiedBy = "api-import"
                };

                timesheets.Add(ts);
                imported++;
            }
        }

        if (timesheets.Any())
        {
            await _context.Timesheets.AddRangeAsync(timesheets);
            await _context.SaveChangesAsync();
        }

        return Ok(new { imported, skipped, message = $"{imported} imported, {skipped} skipped (duplicates/invalid)." });
    }


    [HttpPost("import-csv-depr")]

    public async Task<IActionResult> ImportTimesheetCsv(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        var timesheets = new List<Timesheet>();
        var skippedRecords = new List<string>();
        int imported = 0, skipped = 0;

        using (var stream = file.OpenReadStream())
        using (var reader = new StreamReader(stream))
        {
            int row = 0;
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                row++;

                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = line.Split(',');

                if (cols.Length < 18) // minimum required columns check
                {
                    skipped++;
                    skippedRecords.Add(line);
                    continue;
                }

                string[] formats = { "M/d/yyyy", "M-d-yyyy" }; // ✅ allow both formats

                DateOnly? timesheetDate = null;
                if (DateOnly.TryParseExact(cols[0],
                        formats,                          // multiple formats allowed
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsedDate))
                {
                    timesheetDate = parsedDate;
                }

                var employeeId = cols[1]?.Trim();
                var TimesheetTypeCode = cols[2]?.Trim(); // FIX: use cols[2], not cols[1]

                if (string.IsNullOrEmpty(employeeId) || timesheetDate == null)
                {
                    skipped++;
                    skippedRecords.Add(line);
                    continue;
                }

                // Check for duplicate
                bool exists = _context.Timesheets
                    .Any(t => t.EmployeeId == employeeId &&
                              t.TimesheetDate == timesheetDate &&
                              t.TimesheetTypeCode == TimesheetTypeCode);

                if (exists)
                {
                    skipped++;
                    skippedRecords.Add(line);
                    continue;
                }

                var ts = new Timesheet
                {
                    TimesheetDate = timesheetDate,
                    EmployeeId = employeeId,
                    TimesheetTypeCode = cols[2],
                    WorkingState = cols[3],
                    FiscalYear = int.TryParse(cols[4], out var fy) ? fy : 0,
                    Period = int.TryParse(cols[5], out var per) ? per : 0,
                    Subperiod = int.TryParse(cols[6], out var sp) ? sp : (int?)null,
                    CorrectingRefDate = TryParseDate(cols[7]),
                    PayType = cols[8],
                    GeneralLaborCategory = cols[9],
                    TimesheetLineTypeCode = cols[10],
                    LaborCostAmount = decimal.TryParse(cols[11], out var cost) ? cost : (decimal?)null,
                    Hours = decimal.TryParse(cols[12], out var hrs) ? hrs : (decimal?)null,
                    WorkersCompCode = cols[13],
                    LaborLocationCode = cols[14],
                    OrganizationId = cols[15],
                    AccountId = cols[16],
                    ProjectId = cols[17],
                    ProjectLaborCategory = cols.Length > 18 ? cols[18] : null,
                    ReferenceNumber1 = cols.Length > 19 ? cols[19] : null,
                    ReferenceNumber2 = cols.Length > 20 ? cols[20] : null,
                    OrganizationAbbreviation = cols.Length > 21 ? cols[21] : null,
                    ProjectAbbreviation = cols.Length > 22 ? cols[22] : null,
                    SequenceNumber = int.TryParse(cols.Length > 23 ? cols[23] : null, out var seq) ? seq : (int?)null,
                    EffectiveBillingDate = TryParseDate(cols.Length > 24 ? cols[24] : null),
                    ProjectAccountAbbrev = cols.Length > 25 ? cols[25] : null,
                    MultiStateCode = cols.Length > 26 ? cols[26] : null,
                    ReferenceSequenceNum = int.TryParse(cols.Length > 27 ? cols[27] : null, out var refSeq) ? refSeq : (int?)null,
                    TimesheetLineDate = TryParseDate(cols.Length > 28 ? cols[28] : null),
                    Notes = cols.Length > 29 ? cols[29] : null,
                    CreatedBy = "api-import",
                    ModifiedBy = "api-import"
                };

                timesheets.Add(ts);
                imported++;
            }
        }

        if (timesheets.Any())
        {
            var TsDate = timesheets
                .Where(t => t.TimesheetDate.HasValue &&
                            t.TimesheetDate.Value.DayOfWeek == DayOfWeek.Sunday) // Only Sundays
                .Select(t => t.TimesheetDate.Value)                        // Strip time
                .Distinct()                                                     // Remove duplicates
                .OrderBy(d => d)                                                // Optional: sort
                .ToList();
            if (TsDate.Count() == 1)
            {
                foreach (var t in timesheets)
                {
                    if (t.TimesheetDate.HasValue)
                    {
                        t.TimesheetDate = TsDate[0];
                    }
                }
            }

            await _context.Timesheets.AddRangeAsync(timesheets);
            await _context.SaveChangesAsync();
        }

        // ✅ Return skipped records as downloadable CSV
        if (skippedRecords.Any())
        {
            var csvContent = string.Join(Environment.NewLine, skippedRecords);
            var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
            var outputFile = new FileContentResult(bytes, "text/csv")
            {
                FileDownloadName = "SkippedRecords.csv"
            };

            return File(bytes, "text/csv", "SkippedRecords.csv");
        }

        return Ok(new { imported, skipped, message = $"{imported} imported, {skipped} skipped." });
    }
    //public async Task<IActionResult> ImportTimesheetCsv(IFormFile file)
    //{
    //    if (file == null || file.Length == 0)
    //        return BadRequest("No file uploaded.");

    //    var timesheets = new List<Timesheet>();
    //    var skippedRecords = new List<string>();
    //    int imported = 0, skipped = 0;

    //    using (var stream = file.OpenReadStream())
    //    using (var reader = new StreamReader(stream))
    //    {
    //        int row = 0;
    //        while (!reader.EndOfStream)
    //        {
    //            var line = reader.ReadLine();
    //            row++;

    //            if (string.IsNullOrWhiteSpace(line)) continue;

    //            var cols = line.Split(',');

    //            if (cols.Length < 18) // minimum required columns check
    //            {
    //                skipped++;
    //                skippedRecords.Add(line);
    //                continue;
    //            }

    //            string[] formats = { "M/d/yyyy", "M-d-yyyy" }; // ✅ allow both formats

    //            DateOnly? timesheetDate = null;
    //            if (DateOnly.TryParseExact(cols[0],
    //                    formats,                          // multiple formats allowed
    //                    CultureInfo.InvariantCulture,
    //                    DateTimeStyles.None,
    //                    out var parsedDate))
    //            {
    //                timesheetDate = parsedDate;
    //            }

    //            var employeeId = cols[1]?.Trim();
    //            var TimesheetTypeCode = cols[2]?.Trim(); // FIX: use cols[2], not cols[1]

    //            if (string.IsNullOrEmpty(employeeId) || timesheetDate == null)
    //            {
    //                skipped++;
    //                skippedRecords.Add(line);
    //                continue;
    //            }

    //            // Check for duplicate
    //            bool exists = _context.Timesheets
    //                .Any(t => t.EmployeeId == employeeId &&
    //                          t.TimesheetDate == timesheetDate &&
    //                          t.TimesheetTypeCode == TimesheetTypeCode);

    //            if (exists)
    //            {
    //                skipped++;
    //                skippedRecords.Add(line);
    //                continue;
    //            }

    //            var ts = new Timesheet
    //            {
    //                TimesheetDate = timesheetDate,
    //                EmployeeId = employeeId,
    //                TimesheetTypeCode = cols[2],
    //                WorkingState = cols[3],
    //                FiscalYear = int.TryParse(cols[4], out var fy) ? fy : 0,
    //                Period = int.TryParse(cols[5], out var per) ? per : 0,
    //                Subperiod = int.TryParse(cols[6], out var sp) ? sp : (int?)null,
    //                CorrectingRefDate = TryParseDate(cols[7]),
    //                PayType = cols[8],
    //                GeneralLaborCategory = cols[9],
    //                TimesheetLineTypeCode = cols[10],
    //                LaborCostAmount = decimal.TryParse(cols[11], out var cost) ? cost : (decimal?)null,
    //                Hours = decimal.TryParse(cols[12], out var hrs) ? hrs : (decimal?)null,
    //                WorkersCompCode = cols[13],
    //                LaborLocationCode = cols[14],
    //                OrganizationId = cols[15],
    //                AccountId = cols[16],
    //                ProjectId = cols[17],
    //                ProjectLaborCategory = cols.Length > 18 ? cols[18] : null,
    //                ReferenceNumber1 = cols.Length > 19 ? cols[19] : null,
    //                ReferenceNumber2 = cols.Length > 20 ? cols[20] : null,
    //                OrganizationAbbreviation = cols.Length > 21 ? cols[21] : null,
    //                ProjectAbbreviation = cols.Length > 22 ? cols[22] : null,
    //                SequenceNumber = int.TryParse(cols.Length > 23 ? cols[23] : null, out var seq) ? seq : (int?)null,
    //                EffectiveBillingDate = TryParseDate(cols.Length > 24 ? cols[24] : null),
    //                ProjectAccountAbbrev = cols.Length > 25 ? cols[25] : null,
    //                MultiStateCode = cols.Length > 26 ? cols[26] : null,
    //                ReferenceSequenceNum = int.TryParse(cols.Length > 27 ? cols[27] : null, out var refSeq) ? refSeq : (int?)null,
    //                TimesheetLineDate = TryParseDate(cols.Length > 28 ? cols[28] : null),
    //                Notes = cols.Length > 29 ? cols[29] : null,
    //                CreatedBy = "api-import",
    //                ModifiedBy = "api-import"
    //            };

    //            timesheets.Add(ts);
    //            imported++;
    //        }
    //    }

    //    if (timesheets.Any())
    //    {
    //        var TsDate = timesheets
    //            .Where(t => t.TimesheetDate.HasValue &&
    //                        t.TimesheetDate.Value.DayOfWeek == DayOfWeek.Sunday) // Only Sundays
    //            .Select(t => t.TimesheetDate.Value)                        // Strip time
    //            .Distinct()                                                     // Remove duplicates
    //            .OrderBy(d => d)                                                // Optional: sort
    //            .ToList();
    //        if (TsDate.Count() == 1)
    //        {
    //            foreach (var t in timesheets)
    //            {
    //                if (t.TimesheetDate.HasValue)
    //                {
    //                    t.TimesheetDate = TsDate[0];
    //                }
    //            }
    //        }

    //        await _context.Timesheets.AddRangeAsync(timesheets);
    //        await _context.SaveChangesAsync();
    //    }

    //    // ✅ Return skipped records as downloadable CSV
    //    if (skippedRecords.Any())
    //    {
    //        var csvContent = string.Join(Environment.NewLine, skippedRecords);
    //        var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
    //        var outputFile = new FileContentResult(bytes, "text/csv")
    //        {
    //            FileDownloadName = "SkippedRecords.csv"
    //        };

    //        return File(bytes, "text/csv", "SkippedRecords.csv");
    //    }

    //    return Ok(new { imported, skipped, message = $"{imported} imported, {skipped} skipped." });
    //}

    private static readonly string[] DateFormats = { "M/d/yyyy", "M-d-yyyy" };

    private static DateOnly? TryParseDate(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        return DateOnly.TryParseExact(input.Trim(),
            DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var result)
            ? result
            : null;

    }

    //public DateOnly NextSunday(DateOnly date)
    //{
    //    // Convert to DateTime for easy calculation
    //    var dateTime = date.ToDateTime(TimeOnly.MinValue);
    //    int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)dateTime.DayOfWeek + 7) % 7;
    //    return DateOnly.FromDateTime(dateTime.AddDays(daysUntilSunday));
    //}


    [HttpPost("import-csv-s3")]
    public async Task<IActionResult> ImportTimesheetsS3Async(string filename, string Username)
    {
        var timesheets = new List<Timesheet>();
        var skippedRecords = new List<string>();
        int imported = 0, skipped = 0;

        // Cache existing timesheets to reduce DB hits
        var existingRecords = _context.Timesheets
            .Select(t => new { t.EmployeeId, t.TimesheetDate, t.TimesheetTypeCode, t.ProjectId, t.Period, t.FiscalYear, t.SequenceNumber })
            .ToHashSet();




        //using var stream = file.OpenReadStream();
        //using var reader = new StreamReader(stream);
        // Create S3 client with credentials and region
        var credentials = new BasicAWSCredentials(AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY);
        var s3Client = new AmazonS3Client(credentials, Amazon.RegionEndpoint.GetBySystemName(Region));
        string csvText;
        using (var response = await s3Client.GetObjectAsync(BucketName, filename))
        using (var reader = new StreamReader(response.ResponseStream))

        {
            int row = 0;
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                row++;

                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = line.Split(',');

                if (cols.Length < 18)
                {
                    skipped++;
                    skippedRecords.Add(line);
                    continue;
                }

                var timesheetDate = TryParseDate(cols[0]);
                int? SequenceNo = Convert.ToInt16(cols[23]?.Trim());
                var employeeId = cols.ElementAtOrDefault(1)?.Trim();
                var timesheetTypeCode = cols.ElementAtOrDefault(2)?.Trim();
                var ProjectId = cols.ElementAtOrDefault(17)?.Trim();
                int Month = Convert.ToInt16(cols.ElementAtOrDefault(5)?.Trim());
                int Year = Convert.ToInt16(cols.ElementAtOrDefault(4)?.Trim());
                var dateTime = timesheetDate.GetValueOrDefault().ToDateTime(TimeOnly.MinValue);

                int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)dateTime.DayOfWeek + 7) % 7;
                timesheetDate = DateOnly.FromDateTime(dateTime.AddDays(daysUntilSunday));

                //if (string.IsNullOrEmpty(employeeId) || timesheetDate == null)
                //{
                //    skipped++;
                //    skippedRecords.Add(line);
                //    continue;
                //}

                // ✅ Fast in-memory duplicate check
                var key = new { EmployeeId = employeeId, TimesheetDate = timesheetDate, TimesheetTypeCode = timesheetTypeCode, ProjectId = ProjectId, Period = Month, FiscalYear = Year, SequenceNumber = SequenceNo };
                if (existingRecords.Contains(key))
                {
                    skipped++;
                    skippedRecords.Add(line);
                    continue;
                }

                // Add to cache to prevent duplicates within same import batch
                //existingRecords.Add(key);

                var ts = new Timesheet
                {
                    TimesheetDate = timesheetDate,
                    EmployeeId = employeeId,
                    TimesheetTypeCode = timesheetTypeCode,
                    WorkingState = cols.ElementAtOrDefault(3),
                    FiscalYear = int.TryParse(cols.ElementAtOrDefault(4), out var fy) ? fy : 0,
                    Period = int.TryParse(cols.ElementAtOrDefault(5), out var per) ? per : 0,
                    Subperiod = int.TryParse(cols.ElementAtOrDefault(6), out var sp) ? sp : (int?)null,
                    CorrectingRefDate = TryParseDate(cols.ElementAtOrDefault(7)),
                    PayType = cols.ElementAtOrDefault(8),
                    GeneralLaborCategory = cols.ElementAtOrDefault(9),
                    TimesheetLineTypeCode = cols.ElementAtOrDefault(10),
                    LaborCostAmount = decimal.TryParse(cols.ElementAtOrDefault(11), out var cost) ? cost : (decimal?)null,
                    Hours = decimal.TryParse(cols.ElementAtOrDefault(12), out var hrs) ? hrs : (decimal?)null,
                    WorkersCompCode = cols.ElementAtOrDefault(13),
                    LaborLocationCode = cols.ElementAtOrDefault(14),
                    OrganizationId = cols.ElementAtOrDefault(15),
                    AccountId = cols.ElementAtOrDefault(16),
                    ProjectId = cols.ElementAtOrDefault(17),
                    ProjectLaborCategory = cols.ElementAtOrDefault(18),
                    ReferenceNumber1 = cols.ElementAtOrDefault(19),
                    ReferenceNumber2 = cols.ElementAtOrDefault(20),
                    OrganizationAbbreviation = cols.ElementAtOrDefault(21),
                    ProjectAbbreviation = cols.ElementAtOrDefault(22),
                    SequenceNumber = int.TryParse(cols.ElementAtOrDefault(23), out var seq) ? seq : (int?)null,
                    EffectiveBillingDate = TryParseDate(cols.ElementAtOrDefault(24)),
                    ProjectAccountAbbrev = cols.ElementAtOrDefault(25),
                    MultiStateCode = cols.ElementAtOrDefault(26),
                    ReferenceSequenceNum = int.TryParse(cols.ElementAtOrDefault(27), out var refSeq) ? refSeq : (int?)null,
                    TimesheetLineDate = TryParseDate(cols.ElementAtOrDefault(28)),
                    Notes = cols.ElementAtOrDefault(29),
                    CreatedBy = Username,
                    ModifiedBy = Username,
                    BatchId = new string((employeeId ?? "").Take(3).ToArray())
                                      + (cols.ElementAtOrDefault(4) ?? "")
                                      + (cols.ElementAtOrDefault(5) ?? "")
                                      + (cols.ElementAtOrDefault(23) ?? "")
                };

                timesheets.Add(ts);
                imported++;
            }
        }
        // ✅ Bulk insert for performance (if supported)
        if (timesheets.Any())
        {
            var TsDate = timesheets
                .Where(t => t.TimesheetDate.HasValue &&
                            t.TimesheetDate.Value.DayOfWeek == DayOfWeek.Sunday) // Only Sundays
                .Select(t => t.TimesheetDate.Value)                        // Strip time
                .Distinct()                                                     // Remove duplicates
                .OrderBy(d => d)                                                // Optional: sort
                .ToList();
            if (TsDate.Count() == 1)
            {
                foreach (var t in timesheets)
                {
                    t.TimesheetDate = TsDate[0];
                    t.CreatedDate = DateTime.SpecifyKind(t.CreatedDate, DateTimeKind.Unspecified);
                    t.ModifiedDate = DateTime.SpecifyKind(t.ModifiedDate, DateTimeKind.Unspecified);
                }
            }
            try
            {
                await _context.BulkInsertAsync(timesheets);
                await _context.SaveChangesAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "23503")
            {
                // 23503 = foreign key violation
                var match = System.Text.RegularExpressions.Regex.Match(ex.Detail ?? "", @"Key \(employee_id\)=\((.*?)\)");
                var employeeId = match.Success ? match.Groups[1].Value : "unknown";

                return BadRequest(new
                {
                    Message = $"Import failed: Employee ID '{employeeId}' does not exist in the Employee table. " +
                              "Please check your data and try again."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "An unexpected error occurred during import." });
            }
        }

        // ✅ Return skipped records as downloadable CSV
        if (skippedRecords.Any())
        {
            var csvContent = string.Join(Environment.NewLine, skippedRecords);
            var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
            var outputFile = new FileContentResult(bytes, "text/csv")
            {
                FileDownloadName = "SkippedRecords.csv"
            };

            return File(bytes, "text/csv", "SkippedRecords.csv");
        }

        return Ok(new { imported, skipped, message = $"{imported} imported, {skipped} skipped." });
    }
    [HttpDelete("BulkDeleteTimesheets")]
    public async Task<(bool IsSuccess, string Message)> BulkDeleteTimesheetsAsync(List<int> timesheetIds, string username)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var timesheets = await _context.Timesheets
           .Where(t => timesheetIds.Contains(t.TimesheetId)).Include(t => t.ApprovalRequests)
           .ToListAsync();

            if (timesheets.Count == 0)
                return (false, "No matching timesheets found.");


            // Prepare archive entities
            var archives = timesheets.Select(ts => new TimesheetArchive
            {
                TimesheetId = ts.TimesheetId,
                TimesheetDate = ts.TimesheetDate,
                EmployeeId = ts.EmployeeId,
                TimesheetTypeCode = ts.TimesheetTypeCode,
                WorkingState = ts.WorkingState,
                FiscalYear = ts.FiscalYear,
                Period = ts.Period,
                Subperiod = ts.Subperiod,
                CorrectingRefDate = ts.CorrectingRefDate,
                PayType = ts.PayType,
                GeneralLaborCategory = ts.GeneralLaborCategory,
                TimesheetLineTypeCode = ts.TimesheetLineTypeCode,
                LaborCostAmount = ts.LaborCostAmount,
                Hours = ts.Hours,
                WorkersCompCode = ts.WorkersCompCode,
                Status = ts.Status,
                LaborLocationCode = ts.LaborLocationCode,
                OrganizationId = ts.OrganizationId,
                AccountId = ts.AccountId,
                ProjectId = ts.ProjectId,
                ProjectLaborCategory = ts.ProjectLaborCategory,
                ReferenceNumber1 = ts.ReferenceNumber1,
                ReferenceNumber2 = ts.ReferenceNumber2,
                OrganizationAbbreviation = ts.OrganizationAbbreviation,
                ProjectAbbreviation = ts.ProjectAbbreviation,
                SequenceNumber = ts.SequenceNumber,
                EffectiveBillingDate = ts.EffectiveBillingDate,
                ProjectAccountAbbrev = ts.ProjectAccountAbbrev,
                MultiStateCode = ts.MultiStateCode,
                ReferenceSequenceNum = ts.ReferenceSequenceNum,
                TimesheetLineDate = ts.TimesheetLineDate,
                Notes = ts.Notes,
                DeletedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow,
                DeletedBy = username,
                ModifiedBy = username,
                Rowversion = 1,
                BatchId = ts.BatchId
            }).ToList();

            // 1. Add to archive
            await _context.TimesheetArchives.AddRangeAsync(archives);

            // 2. Delete from main table
            _context.Timesheets.RemoveRange(timesheets);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return (true, "Bulk archive + delete successful.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return (false, $"Error: {ex.Message}");
        }
    }



    [HttpPost("import-csv")]
    public async Task<IActionResult> ImportTimesheetsAsync(IFormFile file, string Username)
    {
        var timesheets = new List<Timesheet>();
        var skippedRecords = new List<string>();
        int imported = 0, skipped = 0;

        // Cache existing timesheets to reduce DB hits
        var existingRecords = _context.Timesheets
            .Select(t => new { t.EmployeeId, t.TimesheetDate, t.TimesheetTypeCode, t.ProjectId, t.Period, t.FiscalYear, t.SequenceNumber })
            .ToHashSet();

        using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);

        int row = 0;
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            row++;

            if (string.IsNullOrWhiteSpace(line)) continue;

            var cols = line.Split(',');

            if (cols.Length < 18)
            {
                skipped++;
                skippedRecords.Add(line);
                continue;
            }

            var timesheetDate = TryParseDate(cols[0]);
            int? SequenceNo = Convert.ToInt16(cols[23]?.Trim());
            var employeeId = cols.ElementAtOrDefault(1)?.Trim();
            var timesheetTypeCode = cols.ElementAtOrDefault(2)?.Trim();
            var ProjectId = cols.ElementAtOrDefault(17)?.Trim();
            int Month = Convert.ToInt16(cols.ElementAtOrDefault(5)?.Trim());
            int Year = Convert.ToInt16(cols.ElementAtOrDefault(4)?.Trim());
            var dateTime = timesheetDate.GetValueOrDefault().ToDateTime(TimeOnly.MinValue);

            int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)dateTime.DayOfWeek + 7) % 7;
            timesheetDate = DateOnly.FromDateTime(dateTime.AddDays(daysUntilSunday));

            if (string.IsNullOrEmpty(employeeId) || timesheetDate == null)
            {
                skipped++;
                skippedRecords.Add(line);
                continue;
            }

            // ✅ Fast in-memory duplicate check
            var key = new { EmployeeId = employeeId, TimesheetDate = timesheetDate, TimesheetTypeCode = timesheetTypeCode, ProjectId = ProjectId, Period = Month, FiscalYear = Year, SequenceNumber = SequenceNo };
            if (existingRecords.Contains(key))
            {
                skipped++;
                skippedRecords.Add(line);
                continue;
            }

            // Add to cache to prevent duplicates within same import batch
            //existingRecords.Add(key);

            var ts = new Timesheet
            {
                TimesheetDate = timesheetDate,
                EmployeeId = employeeId,
                TimesheetTypeCode = timesheetTypeCode,
                WorkingState = cols.ElementAtOrDefault(3),
                FiscalYear = int.TryParse(cols.ElementAtOrDefault(4), out var fy) ? fy : 0,
                Period = int.TryParse(cols.ElementAtOrDefault(5), out var per) ? per : 0,
                Subperiod = int.TryParse(cols.ElementAtOrDefault(6), out var sp) ? sp : (int?)null,
                CorrectingRefDate = TryParseDate(cols.ElementAtOrDefault(7)),
                PayType = cols.ElementAtOrDefault(8),
                GeneralLaborCategory = cols.ElementAtOrDefault(9),
                TimesheetLineTypeCode = cols.ElementAtOrDefault(10),
                LaborCostAmount = decimal.TryParse(cols.ElementAtOrDefault(11), out var cost) ? cost : (decimal?)null,
                Hours = decimal.TryParse(cols.ElementAtOrDefault(12), out var hrs) ? hrs : (decimal?)null,
                WorkersCompCode = cols.ElementAtOrDefault(13),
                LaborLocationCode = cols.ElementAtOrDefault(14),
                OrganizationId = cols.ElementAtOrDefault(15),
                AccountId = cols.ElementAtOrDefault(16),
                ProjectId = cols.ElementAtOrDefault(17),
                ProjectLaborCategory = cols.ElementAtOrDefault(18),
                ReferenceNumber1 = cols.ElementAtOrDefault(19),
                ReferenceNumber2 = cols.ElementAtOrDefault(20),
                OrganizationAbbreviation = cols.ElementAtOrDefault(21),
                ProjectAbbreviation = cols.ElementAtOrDefault(22),
                SequenceNumber = int.TryParse(cols.ElementAtOrDefault(23), out var seq) ? seq : (int?)null,
                EffectiveBillingDate = TryParseDate(cols.ElementAtOrDefault(24)),
                ProjectAccountAbbrev = cols.ElementAtOrDefault(25),
                MultiStateCode = cols.ElementAtOrDefault(26),
                ReferenceSequenceNum = int.TryParse(cols.ElementAtOrDefault(27), out var refSeq) ? refSeq : (int?)null,
                TimesheetLineDate = TryParseDate(cols.ElementAtOrDefault(28)),
                Notes = cols.ElementAtOrDefault(29),
                CreatedBy = Username,
                ModifiedBy = Username
            };

            timesheets.Add(ts);
            imported++;
        }

        // ✅ Bulk insert for performance (if supported)
        if (timesheets.Any())
        {
            var TsDate = timesheets
                .Where(t => t.TimesheetDate.HasValue &&
                            t.TimesheetDate.Value.DayOfWeek == DayOfWeek.Sunday) // Only Sundays
                .Select(t => t.TimesheetDate.Value)                        // Strip time
                .Distinct()                                                     // Remove duplicates
                .OrderBy(d => d)                                                // Optional: sort
                .ToList();
            if (TsDate.Count() == 1)
            {
                foreach (var t in timesheets)
                {
                    t.TimesheetDate = TsDate[0];
                    t.CreatedDate = DateTime.SpecifyKind(t.CreatedDate, DateTimeKind.Unspecified);
                    t.ModifiedDate = DateTime.SpecifyKind(t.ModifiedDate, DateTimeKind.Unspecified);
                }
            }

            await _context.BulkInsertAsync(timesheets);
            await _context.SaveChangesAsync();
        }

        // ✅ Return skipped records as downloadable CSV
        if (skippedRecords.Any())
        {
            var csvContent = string.Join(Environment.NewLine, skippedRecords);
            var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
            var outputFile = new FileContentResult(bytes, "text/csv")
            {
                FileDownloadName = "SkippedRecords.csv"
            };

            return File(bytes, "text/csv", "SkippedRecords.csv");
        }

        return Ok(new { imported, skipped, message = $"{imported} imported, {skipped} skipped." });
    }

    [HttpPost("import-projects-csv")]
    public async Task<IActionResult> ImportProjectsAsync(IFormFile file, string Username)
    {
        var Projects = new List<Project>();
        var skippedRecords = new List<string>();
        var changedPMProjects = new List<Project>();  // ✅ Track PM changes
        int imported = 0, skipped = 0;

        // Cache existing projects for fast lookups
        var existingProjects = await _context.Projects
            .Select(p => new { p.ProjectId, p.ProjectManagerId, p.ProjectManagerName, p.Email })
            .ToDictionaryAsync(p => p.ProjectId, p => p);

        using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);

        int row = 0;
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false
        });

        while (csv.Read())
        {
            var cols = csv.Parser.Record;

            var ProjectId = cols.ElementAtOrDefault(0)?.Trim();
            var ProjectName = cols.ElementAtOrDefault(1)?.Trim();
            var ProjectType = cols.ElementAtOrDefault(2)?.Trim();
            var OrdId = cols.ElementAtOrDefault(3)?.Trim();
            var PmId = cols.ElementAtOrDefault(4)?.Trim();
            var PmName = cols.ElementAtOrDefault(5)?.Trim();
            var Email = cols.ElementAtOrDefault(6)?.Trim();
            var Status = cols.ElementAtOrDefault(7)?.Trim();

            if (string.IsNullOrEmpty(ProjectId))
            {
                using (var writer = new StringWriter())
                using (var csvrow = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    csvrow.WriteField(cols);   // Write array as a single row
                    csvrow.NextRecord();
                    string csvString = writer.ToString();
                    skipped++;
                    skippedRecords.Add(writer.ToString());
                    continue;
                }
            }

            // ✅ Check if project exists
            if (existingProjects.TryGetValue(ProjectId, out var existing))
            {
                // ✅ Detect PM change
                if (!string.Equals(existing.ProjectManagerId, PmId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(existing.ProjectManagerName, PmName, StringComparison.OrdinalIgnoreCase))
                {
                    changedPMProjects.Add(new Project
                    {
                        ProjectId = ProjectId,
                        ProjectName = ProjectName,
                        ProjectType = ProjectType,
                        OwningOrgId = OrdId,
                        ProjectManagerId = PmId,
                        ProjectManagerName = PmName,
                        Status = Status,
                        Email = Email
                    });
                }
                using (var writer = new StringWriter())
                using (var csvrow = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    csvrow.WriteField(cols);   // Write array as a single row
                    csvrow.NextRecord();
                    string csvString = writer.ToString();
                    skipped++;
                    skippedRecords.Add(writer.ToString());
                    continue;
                }
            }

            // ✅ Add new project
            var Project = new Project
            {
                ProjectManagerId = PmId,
                ProjectId = ProjectId,
                ProjectName = ProjectName,
                OwningOrgId = OrdId,
                ProjectManagerName = PmName,
                ProjectType = ProjectType,
                Email = Email,
                Status = Status
            };

            Projects.Add(Project);
            imported++;
        }

        // ✅ Bulk insert for new projects
        if (Projects.Any())
        {
            await _context.BulkInsertAsync(Projects);
            await _context.SaveChangesAsync();

            var users = Projects
                .Select(po => new { po.ProjectManagerId, po.ProjectManagerName, po.Email, po.Status })
                .Where(id => id != null)
                .Distinct()
                .Select(id => new User
                {
                    Username = id.ProjectManagerId,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = false,
                    FullName = id.ProjectManagerName,
                    //ProjecId = id.ProjectId,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
                    //ProjectName = id.ProjectName,
                    Role = "User",
                    //Email = "",
                    FirstLogin = true,
                    Email = id.Email ?? ""
                })
                .ToList();

            if (users.Count > 0)
                await _repository.AddUsersAsync(users);
        }

        // ✅ Optionally: Export changed PM projects as a CSV for review
        if (changedPMProjects.Any())
        {
            var csvLines = new List<string> { "employeeId,OldPM,NewPM,OldPMName,NewPMName" };
            List<string> oldUsers = new List<string>();
            foreach (var p in changedPMProjects)
            {
                var old = existingProjects[p.ProjectId];
                csvLines.Add($"{p.ProjectId},{old.ProjectManagerId},{p.ProjectManagerId},{old.ProjectManagerName},{p.ProjectManagerName}");

                var filteredProjects = existingProjects
                        .Where(p => p.Value.ProjectManagerId == old.ProjectManagerId)
                        .Select(p => new { p.Key, p.Value.ProjectManagerId })
                        .ToList();
                if (filteredProjects.Count() <= 1)
                    oldUsers.Add(old.ProjectManagerId);
            }
            if (oldUsers.Count > 0)
            {
                var users = await _context.Users.Where(u => oldUsers.Contains(u.Username)).ToListAsync();
                foreach (var user in users)
                {
                    user.IsActive = false;
                }
                await _context.BulkInsertOrUpdateAsync(users);
                await _context.SaveChangesAsync();
            }
            var existingusers = await _context.Users.Where(u => changedPMProjects.Select(p => p.ProjectManagerId).Contains(u.Username)).Select(u => u.Username).ToListAsync();
            if (existingusers.Count > 0)
            {
                // 1️⃣ Get all PM usernames from changed projects
                var changedPmIds = changedPMProjects
                    .Select(p => p.ProjectManagerId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct()
                    .ToList();

                // 3️⃣ Find PMs that are NOT in Users table
                var missingUsers = changedPMProjects
                    .Where(p => !existingusers.Contains(p.ProjectManagerId))
                    .DistinctBy(p => p.ProjectManagerId) // requires .NET 6+
                    .ToList();

                var newUsers = missingUsers
                    .Select(po => new { po.ProjectManagerId, po.ProjectId, po.ProjectManagerName, po.ProjectName, po.Email, po.Status })
                    .Where(id => id != null)
                    .Distinct()
                    .Select(id => new User
                    {
                        Username = id.ProjectManagerId,
                        CreatedAt = DateTime.UtcNow,
                        IsActive = false,
                        FullName = id.ProjectManagerName,
                        ProjecId = id.ProjectId,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
                        ProjectName = id.ProjectName,
                        Role = "User",
                        Email = "",
                        FirstLogin = true
                        //Email = id.Email ?? ""
                    })
                    .ToList();

                if (newUsers.Count > 0)
                    await _repository.AddUsersAsync(newUsers);

            }
            else
            {
                var newUsers = changedPMProjects
                    .Select(po => new { po.ProjectManagerId, po.ProjectId, po.ProjectManagerName, po.ProjectName, po.Email, po.Status })
                    .Where(id => id != null)
                    .Distinct()
                    .Select(id => new User
                    {
                        Username = id.ProjectManagerId,
                        CreatedAt = DateTime.UtcNow,
                        IsActive = false,
                        FullName = id.ProjectManagerName,
                        ProjecId = id.ProjectId,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
                        ProjectName = id.ProjectName,
                        Role = "User",
                        //Email = "",
                        FirstLogin = true,
                        Email = id.Email ?? ""
                    })
                    .ToList();

                if (newUsers.Count > 0)
                    await _repository.AddUsersAsync(newUsers);
            }

            await _context.BulkInsertOrUpdateAsync(changedPMProjects);
            await _context.SaveChangesAsync();


            var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, csvLines));
            return File(bytes, "text/csv", "ChangedPMProjects.csv");
        }

        // ✅ Return skipped records as downloadable CSV
        if (skippedRecords.Any())
        {
            var csvContent = string.Join(Environment.NewLine, skippedRecords);
            var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
            return File(bytes, "text/csv", "SkippedProjects.csv");
        }

        return Ok(new { imported, skipped, changedPMCount = changedPMProjects.Count, message = $"{imported} imported, {skipped} skipped, {changedPMProjects.Count} PMs changed." });
    }

    [HttpGet("GetPresignedUrl/{filename}")]
    public string GetPresignedUrl(string filename)
    {
        string url = string.Empty;

        try
        {
            var credentials = new BasicAWSCredentials(AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY);
            s3Client = new AmazonS3Client(credentials, Amazon.RegionEndpoint.GetBySystemName(Region));

            string extension = Path.GetExtension(filename).ToLower();
            string contentType = extension switch
            {
                ".csv" => "text/csv",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream" // fallback
            };

            var request = new GetPreSignedUrlRequest
            {
                BucketName = BucketName,
                Key = filename,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.AddMinutes(EXPIRES_IN_MINUTES),
                ContentType = contentType
            };

            url = s3Client.GetPreSignedURL(request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
        }

        return url;
    }

    //public string GetPresignedUrl(string filename)
    //{
    //    string url = string.Empty;
    //    try
    //    {
    //        var credentials = new BasicAWSCredentials(AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY);
    //        s3Client = new AmazonS3Client(credentials, Amazon.RegionEndpoint.GetBySystemName(Region));
    //        var request = new GetPreSignedUrlRequest
    //        {
    //            BucketName = BucketName,
    //            Key = filename,
    //            Verb = HttpVerb.PUT, // Use PUT for upload; GET for download
    //            Expires = DateTime.UtcNow.Add(TimeSpan.FromMinutes(EXPIRES_IN_MINUTES)),
    //            ContentType = "text/csv"
    //        };

    //        url = s3Client.GetPreSignedURL(request);
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine($"❌ Error: {ex.Message}");
    //    }
    //    return url;
    //}

    [HttpPost("import-projects-csv-s3")]
    public async Task<IActionResult> ImportProjectsS3Async(string filename, string Username)
    {
        var Projects = new List<Project>();
        var skippedRecords = new List<string>();
        var changedPMProjects = new List<Project>();  // ✅ Track PM changes
        int imported = 0, skipped = 0;

        // Cache existing projects for fast lookups
        var existingProjects = await _context.Projects
            .Select(p => new { p.ProjectId, p.ProjectManagerId, p.ProjectManagerName, p.Email })
            .ToDictionaryAsync(p => p.ProjectId, p => p);


        // Create S3 client with credentials and region
        var credentials = new BasicAWSCredentials(AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY);
        var s3Client = new AmazonS3Client(credentials, Amazon.RegionEndpoint.GetBySystemName(Region));

        // 1️⃣ Download CSV file
        Console.WriteLine($"📥 Downloading {filename} from S3 bucket: {BucketName}...");
        string csvText;
        using (var response = await s3Client.GetObjectAsync(BucketName, filename))
        using (var reader = new StreamReader(response.ResponseStream))
        {

            int row = 0;
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            });

            while (csv.Read())
            {
                var cols = csv.Parser.Record;

                var ProjectId = cols.ElementAtOrDefault(0)?.Trim();
                var ProjectName = cols.ElementAtOrDefault(1)?.Trim();
                var ProjectType = cols.ElementAtOrDefault(2)?.Trim();
                var OrdId = cols.ElementAtOrDefault(3)?.Trim();
                var PmId = cols.ElementAtOrDefault(4)?.Trim();
                var PmName = cols.ElementAtOrDefault(5)?.Trim();
                var Email = cols.ElementAtOrDefault(6)?.Trim();
                var Status = cols.ElementAtOrDefault(7)?.Trim();

                if (string.IsNullOrEmpty(ProjectId))
                {
                    using (var writer = new StringWriter())
                    using (var csvrow = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csvrow.WriteField(cols);   // Write array as a single row
                        csvrow.NextRecord();
                        string csvString = writer.ToString();
                        skipped++;
                        skippedRecords.Add(writer.ToString());
                        continue;
                    }
                }

                // ✅ Check if project exists
                if (existingProjects.TryGetValue(ProjectId, out var existing))
                {
                    // ✅ Detect PM change
                    if (!string.Equals(existing.ProjectManagerId, PmId, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existing.ProjectManagerName, PmName, StringComparison.OrdinalIgnoreCase))
                    {
                        changedPMProjects.Add(new Project
                        {
                            ProjectId = ProjectId,
                            ProjectName = ProjectName,
                            ProjectType = ProjectType,
                            OwningOrgId = OrdId,
                            ProjectManagerId = PmId,
                            ProjectManagerName = PmName,
                            Status = Status,
                            Email = Email
                        });
                    }
                    using (var writer = new StringWriter())
                    using (var csvrow = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csvrow.WriteField(cols);   // Write array as a single row
                        csvrow.NextRecord();
                        string csvString = writer.ToString();
                        skipped++;
                        skippedRecords.Add(writer.ToString());
                        continue;
                    }
                }

                // ✅ Add new project
                var Project = new Project
                {
                    ProjectManagerId = PmId,
                    ProjectId = ProjectId,
                    ProjectName = ProjectName,
                    OwningOrgId = OrdId,
                    ProjectManagerName = PmName,
                    ProjectType = ProjectType,
                    Email = Email,
                    Status = Status
                };

                Projects.Add(Project);
                imported++;
            }
        }
        // ✅ Bulk insert for new projects
        if (Projects.Any())
        {
            await _context.BulkInsertAsync(Projects);
            await _context.SaveChangesAsync();

            var users = Projects
                .Select(po => new { po.ProjectManagerId, po.ProjectManagerName, po.Email, po.Status })
                .Where(id => id != null)
                .Distinct()
                .Select(id => new User
                {
                    Username = id.ProjectManagerId,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = false,
                    FullName = id.ProjectManagerName,
                    //ProjecId = id.ProjectId,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
                    //ProjectName = id.ProjectName,
                    Role = "User",
                    //Email = "",
                    FirstLogin = true,
                    Email = id.Email ?? ""
                })
                .ToList();

            if (users.Count > 0)
                await _repository.AddUsersAsync(users);
        }

        // ✅ Optionally: Export changed PM projects as a CSV for review
        if (changedPMProjects.Any())
        {
            var csvLines = new List<string> { "employeeId,OldPM,NewPM,OldPMName,NewPMName" };
            List<string> oldUsers = new List<string>();
            foreach (var p in changedPMProjects)
            {
                var old = existingProjects[p.ProjectId];
                csvLines.Add($"{p.ProjectId},{old.ProjectManagerId},{p.ProjectManagerId},{old.ProjectManagerName},{p.ProjectManagerName}");

                var filteredProjects = existingProjects
                        .Where(p => p.Value.ProjectManagerId == old.ProjectManagerId)
                        .Select(p => new { p.Key, p.Value.ProjectManagerId })
                        .ToList();
                if (filteredProjects.Count() <= 1)
                    oldUsers.Add(old.ProjectManagerId);
            }
            if (oldUsers.Count > 0)
            {
                var users = await _context.Users.Where(u => oldUsers.Contains(u.Username)).ToListAsync();
                foreach (var user in users)
                {
                    user.IsActive = false;
                }
                await _context.BulkInsertOrUpdateAsync(users);
                await _context.SaveChangesAsync();
            }
            var existingusers = await _context.Users.Where(u => changedPMProjects.Select(p => p.ProjectManagerId).Contains(u.Username)).Select(u => u.Username).ToListAsync();
            if (existingusers.Count > 0)
            {
                // 1️⃣ Get all PM usernames from changed projects
                var changedPmIds = changedPMProjects
                    .Select(p => p.ProjectManagerId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct()
                    .ToList();

                // 3️⃣ Find PMs that are NOT in Users table
                var missingUsers = changedPMProjects
                    .Where(p => !existingusers.Contains(p.ProjectManagerId))
                    .DistinctBy(p => p.ProjectManagerId) // requires .NET 6+
                    .ToList();

                var newUsers = missingUsers
                    .Select(po => new { po.ProjectManagerId, po.ProjectId, po.ProjectManagerName, po.ProjectName, po.Email, po.Status })
                    .Where(id => id != null)
                    .Distinct()
                    .Select(id => new User
                    {
                        Username = id.ProjectManagerId,
                        CreatedAt = DateTime.UtcNow,
                        IsActive = false,
                        FullName = id.ProjectManagerName,
                        ProjecId = id.ProjectId,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
                        ProjectName = id.ProjectName,
                        Role = "User",
                        Email = "",
                        FirstLogin = true
                        //Email = id.Email ?? ""
                    })
                    .ToList();

                if (newUsers.Count > 0)
                    await _repository.AddUsersAsync(newUsers);

            }
            else
            {
                var newUsers = changedPMProjects
                    .Select(po => new { po.ProjectManagerId, po.ProjectId, po.ProjectManagerName, po.ProjectName, po.Email, po.Status })
                    .Where(id => id != null)
                    .Distinct()
                    .Select(id => new User
                    {
                        Username = id.ProjectManagerId,
                        CreatedAt = DateTime.UtcNow,
                        IsActive = false,
                        FullName = id.ProjectManagerName,
                        ProjecId = id.ProjectId,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
                        ProjectName = id.ProjectName,
                        Role = "User",
                        //Email = "",
                        FirstLogin = true,
                        Email = id.Email ?? ""
                    })
                    .ToList();

                if (newUsers.Count > 0)
                    await _repository.AddUsersAsync(newUsers);
            }

            await _context.BulkInsertOrUpdateAsync(changedPMProjects);
            await _context.SaveChangesAsync();


            var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, csvLines));
            return File(bytes, "text/csv", "ChangedPMProjects.csv");
        }

        // ✅ Return skipped records as downloadable CSV
        if (skippedRecords.Any())
        {
            var csvContent = string.Join(Environment.NewLine, skippedRecords);
            var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
            return File(bytes, "text/csv", "SkippedProjects.csv");
        }

        return Ok(new { imported, skipped, changedPMCount = changedPMProjects.Count, message = $"{imported} imported, {skipped} skipped, {changedPMProjects.Count} PMs changed." });
    }


    [HttpPost("import-projects-excel-s3")]
    public async Task<IActionResult> ImportProjectsExcelS3Async(string filename, string Username)
    {
        var Projects = new List<Project>();
        var skippedRecords = new List<string>();
        var changedPMProjects = new List<Project>();  // ✅ Track PM changes
        int imported = 0, skipped = 0;
        ExcelHelper excelHelper = new ExcelHelper();

        // Cache existing projects for fast lookups
        var existingProjects = await _context.Projects
            .Select(p => new { p.ProjectId, p.ProjectManagerId, p.ProjectManagerName, p.Email })
            .ToDictionaryAsync(p => p.ProjectId, p => p);


        // Create S3 client with credentials and region
        var credentials = new BasicAWSCredentials(AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY);
        var s3Client = new AmazonS3Client(credentials, Amazon.RegionEndpoint.GetBySystemName(Region));

        // 1️⃣ Download CSV file
        Console.WriteLine($"📥 Downloading {filename} from S3 bucket: {BucketName}...");
        string csvText;
        using (var response = await s3Client.GetObjectAsync(BucketName, filename))
        using (var memoryStream = new MemoryStream())
        {

            await response.ResponseStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            IWorkbook workbook = new XSSFWorkbook(memoryStream);  // XLSX
            ISheet sheet = workbook.GetSheetAt(0);
            for (int i = 1; i <= sheet.LastRowNum; i++)
            {
                var row = sheet.GetRow(i);
                if (row == null) continue;

                var ProjectId = excelHelper.GetString(row.GetCell(0));
                var ProjectName = excelHelper.GetString(row.GetCell(1));
                var ProjectType = excelHelper.GetString(row.GetCell(2));
                var OrdId = excelHelper.GetString(row.GetCell(3));
                var PmId = excelHelper.GetString(row.GetCell(4));
                var PmName = excelHelper.GetString(row.GetCell(5));
                var Email = excelHelper.GetString(row.GetCell(6));
                var Status = excelHelper.GetString(row.GetCell(7));

                if (string.IsNullOrEmpty(ProjectId))
                {
                    using (var writer = new StringWriter())
                    using (var csvrow = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csvrow.WriteField(excelHelper.RowToCsv(row));   // Write array as a single row
                        csvrow.NextRecord();
                        string csvString = writer.ToString();
                        skipped++;
                        skippedRecords.Add(writer.ToString());
                        continue;
                    }
                }

                // ✅ Check if project exists
                if (existingProjects.TryGetValue(ProjectId, out var existing))
                {
                    // ✅ Detect PM change
                    if (!string.Equals(existing.ProjectManagerId, PmId, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existing.ProjectManagerName, PmName, StringComparison.OrdinalIgnoreCase))
                    {
                        changedPMProjects.Add(new Project
                        {
                            ProjectId = ProjectId,
                            ProjectName = ProjectName,
                            ProjectType = ProjectType,
                            OwningOrgId = OrdId,
                            ProjectManagerId = PmId,
                            ProjectManagerName = PmName,
                            Status = Status,
                            Email = Email
                        });
                    }
                    using (var writer = new StringWriter())
                    using (var csvrow = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csvrow.WriteField(excelHelper.RowToCsv(row));   // Write array as a single row
                        csvrow.NextRecord();
                        string csvString = writer.ToString();
                        skipped++;
                        skippedRecords.Add(writer.ToString());
                        continue;
                    }
                }

                // ✅ Add new project
                var Project = new Project
                {
                    ProjectManagerId = PmId,
                    ProjectId = ProjectId,
                    ProjectName = ProjectName,
                    OwningOrgId = OrdId,
                    ProjectManagerName = PmName,
                    ProjectType = ProjectType,
                    Email = Email,
                    Status = Status
                };

                Projects.Add(Project);
                imported++;
            }
        }
        // ✅ Bulk insert for new projects
        if (Projects.Any())
        {
            await _context.BulkInsertAsync(Projects);
            await _context.SaveChangesAsync();

            var users = Projects
                .Select(po => new { po.ProjectManagerId, po.ProjectManagerName, po.Email, po.Status })
                .Where(id => id != null)
                .Distinct()
                .Select(id => new User
                {
                    Username = id.ProjectManagerId,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = false,
                    FullName = id.ProjectManagerName,
                    //ProjecId = id.ProjectId,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
                    //ProjectName = id.ProjectName,
                    Role = "User",
                    //Email = "",
                    FirstLogin = true,
                    Email = id.Email ?? ""
                })
                .ToList();

            if (users.Count > 0)
                await _repository.AddUsersAsync(users);
        }

        // ✅ Optionally: Export changed PM projects as a CSV for review
        if (changedPMProjects.Any())
        {
            var csvLines = new List<string> { "employeeId,OldPM,NewPM,OldPMName,NewPMName" };
            List<string> oldUsers = new List<string>();
            foreach (var p in changedPMProjects)
            {
                var old = existingProjects[p.ProjectId];
                csvLines.Add($"{p.ProjectId},{old.ProjectManagerId},{p.ProjectManagerId},{old.ProjectManagerName},{p.ProjectManagerName}");

                var filteredProjects = existingProjects
                        .Where(p => p.Value.ProjectManagerId == old.ProjectManagerId)
                        .Select(p => new { p.Key, p.Value.ProjectManagerId })
                        .ToList();
                if (filteredProjects.Count() <= 1)
                    oldUsers.Add(old.ProjectManagerId);
            }
            if (oldUsers.Count > 0)
            {
                var users = await _context.Users.Where(u => oldUsers.Contains(u.Username)).ToListAsync();
                foreach (var user in users)
                {
                    user.IsActive = false;
                }
                await _context.BulkInsertOrUpdateAsync(users);
                await _context.SaveChangesAsync();
            }
            var existingusers = await _context.Users.Where(u => changedPMProjects.Select(p => p.ProjectManagerId).Contains(u.Username)).Select(u => u.Username).ToListAsync();
            if (existingusers.Count > 0)
            {
                // 1️⃣ Get all PM usernames from changed projects
                var changedPmIds = changedPMProjects
                    .Select(p => p.ProjectManagerId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct()
                    .ToList();

                // 3️⃣ Find PMs that are NOT in Users table
                var missingUsers = changedPMProjects
                    .Where(p => !existingusers.Contains(p.ProjectManagerId))
                    .DistinctBy(p => p.ProjectManagerId) // requires .NET 6+
                    .ToList();

                var newUsers = missingUsers
                    .Select(po => new { po.ProjectManagerId, po.ProjectId, po.ProjectManagerName, po.ProjectName, po.Email, po.Status })
                    .Where(id => id != null)
                    .Distinct()
                    .Select(id => new User
                    {
                        Username = id.ProjectManagerId,
                        CreatedAt = DateTime.UtcNow,
                        IsActive = false,
                        FullName = id.ProjectManagerName,
                        ProjecId = id.ProjectId,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
                        ProjectName = id.ProjectName,
                        Role = "User",
                        Email = "",
                        FirstLogin = true
                        //Email = id.Email ?? ""
                    })
                    .ToList();

                if (newUsers.Count > 0)
                    await _repository.AddUsersAsync(newUsers);

            }
            else
            {
                var newUsers = changedPMProjects
                    .Select(po => new { po.ProjectManagerId, po.ProjectId, po.ProjectManagerName, po.ProjectName, po.Email, po.Status })
                    .Where(id => id != null)
                    .Distinct()
                    .Select(id => new User
                    {
                        Username = id.ProjectManagerId,
                        CreatedAt = DateTime.UtcNow,
                        IsActive = false,
                        FullName = id.ProjectManagerName,
                        ProjecId = id.ProjectId,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
                        ProjectName = id.ProjectName,
                        Role = "User",
                        //Email = "",
                        FirstLogin = true,
                        Email = id.Email ?? ""
                    })
                    .ToList();

                if (newUsers.Count > 0)
                    await _repository.AddUsersAsync(newUsers);
            }

            await _context.BulkInsertOrUpdateAsync(changedPMProjects);
            await _context.SaveChangesAsync();


            var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, csvLines));
            return File(bytes, "text/csv", "ChangedPMProjects.csv");
        }

        // ✅ Return skipped records as downloadable CSV
        if (skippedRecords.Any())
        {
            var csvContent = string.Join(Environment.NewLine, skippedRecords);
            var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
            return File(bytes, "text/csv", "SkippedProjects.csv");
        }

        return Ok(new { imported, skipped, changedPMCount = changedPMProjects.Count, message = $"{imported} imported, {skipped} skipped, {changedPMProjects.Count} PMs changed." });
    }

    //public async Task<IActionResult> ImportProjectsAsync(IFormFile file, string Username)
    //{
    //    var Projects = new List<Project>();
    //    var skippedRecords = new List<string>();
    //    int imported = 0, skipped = 0;

    //    // Cache existing timesheets to reduce DB hits
    //    var existingRecords = _context.Projects
    //        .Select(t => new { t.ProjectId })
    //        .ToHashSet();

    //    using var stream = file.OpenReadStream();
    //    using var reader = new StreamReader(stream);

    //    int row = 0;
    //    while (!reader.EndOfStream)
    //    {
    //        var line = reader.ReadLine();
    //        row++;

    //        if (string.IsNullOrWhiteSpace(line)) continue;

    //        var cols = line.Split(',');

    //        if (cols.Length < 6)
    //        {
    //            skipped++;
    //            skippedRecords.Add(line);
    //            continue;
    //        }

    //        var ProjectId = cols.ElementAtOrDefault(0)?.Trim();
    //        var ProjectName = cols.ElementAtOrDefault(1)?.Trim();
    //        var ProjectType = cols.ElementAtOrDefault(2)?.Trim();
    //        var OrdId = cols.ElementAtOrDefault(3)?.Trim();
    //        var PmId = cols.ElementAtOrDefault(4)?.Trim();
    //        var PmName = cols.ElementAtOrDefault(5)?.Trim();


    //        if (ProjectId == null)
    //        {
    //            skipped++;
    //            skippedRecords.Add(line);
    //            continue;
    //        }

    //        // ✅ Fast in-memory duplicate check
    //        var key = new { ProjectId = ProjectId};
    //        if (existingRecords.Contains(key))
    //        {
    //            skipped++;
    //            skippedRecords.Add(line);
    //            continue;
    //        }

    //        // Add to cache to prevent duplicates within same import batch
    //        //existingRecords.Add(key);

    //        var Project = new Project
    //        {
    //            ProjectManagerId = PmId,
    //            ProjectId = ProjectId,
    //            ProjectName = ProjectName,
    //            OwningOrgId = OrdId,
    //            ProjectManagerName = PmName,
    //            ProjectType = ProjectType

    //        };

    //        Projects.Add(Project);
    //        imported++;
    //    }

    //    // ✅ Bulk insert for performance (if supported)
    //    if (Projects.Any())
    //    {
    //        await _context.BulkInsertAsync(Projects);
    //        await _context.SaveChangesAsync();

    //        var users = Projects
    //            .Select(po => new { po.ProjectManagerId, po.ProjectId, po.ProjectManagerName, po.ProjectName })
    //            .Where(id => id != null)
    //            .Distinct()
    //            .Select(id => new User
    //            {
    //                Username = id.ProjectManagerId,
    //                CreatedAt = DateTime.UtcNow,
    //                IsActive = false,
    //                FullName = id.ProjectManagerName,
    //                ProjecId   = id.ProjectId,
    //                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
    //                ProjectName = id.ProjectName,
    //                Role= "User",
    //                Email = ""
    //            })
    //            .ToList();

    //        if (users.Count > 0)
    //            await _repository.AddUsersAsync(users);

    //    }

    //    // ✅ Return skipped records as downloadable CSV
    //    if (skippedRecords.Any())
    //    {
    //        var csvContent = string.Join(Environment.NewLine, skippedRecords);
    //        var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
    //        var outputFile = new FileContentResult(bytes, "text/csv")
    //        {
    //            FileDownloadName = "SkippedProjects.csv"
    //        };

    //        return File(bytes, "text/csv", "SkippedProjects.csv");
    //    }

    //    return Ok(new { imported, skipped, message = $"{imported} imported, {skipped} skipped." });
    //}


    [HttpPost("import-employees-excel-s3")]
    public async Task<IActionResult> ImportEmployeesS3Async(string filename, string Username)
    {
        var Employees = new List<Employee>();
        var skippedRecords = new List<string>();
        var changedPMProjects = new List<Employee>();  // ✅ Track PM changes
        int imported = 0, skipped = 0;
        ExcelHelper excelHelper = new ExcelHelper();

        // Cache existing projects for fast lookups
        var existingEmployees = await _context.Employees
            .Select(p => new { p.EmployeeId, p.DisplayedName, p.FirstName, p.LastName })
            .ToDictionaryAsync(p => p.EmployeeId, p => p);


        // Create S3 client with credentials and region
        var credentials = new BasicAWSCredentials(AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY);
        var s3Client = new AmazonS3Client(credentials, Amazon.RegionEndpoint.GetBySystemName(Region));

        // 1️⃣ Download CSV file
        Console.WriteLine($"📥 Downloading {filename} from S3 bucket: {BucketName}...");
        string csvText;
        using (var response = await s3Client.GetObjectAsync(BucketName, filename))
        using (var memoryStream = new MemoryStream())
        {

            await response.ResponseStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            //var stream = file.OpenReadStream();
            IWorkbook workbook = new XSSFWorkbook(memoryStream);  // XLSX
            ISheet sheet = workbook.GetSheetAt(0);

            for (int i = 1; i <= sheet.LastRowNum; i++)
            {
                var row = sheet.GetRow(i);
                if (row == null) continue;

                var employeeId = excelHelper.GetString(row.GetCell(0));
                var DisplayName = excelHelper.GetString(row.GetCell(1));
                var LastName = excelHelper.GetString(row.GetCell(2));
                var FirstName = excelHelper.GetString(row.GetCell(3));
                var Status = excelHelper.GetString(row.GetCell(6));

                if (string.IsNullOrEmpty(employeeId))
                {
                    using (var writer = new StringWriter())
                    using (var csvrow = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csvrow.WriteField(excelHelper.RowToCsv(row));   // Write array as a single row
                        csvrow.NextRecord();
                        string csvString = writer.ToString();
                        skipped++;
                        skippedRecords.Add(writer.ToString());
                        continue;
                    }
                }

                // ✅ Check if project exists
                if (existingEmployees.TryGetValue(employeeId, out var existing))
                {
                    // ✅ Detect PM change
                    if (!string.Equals(existing.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))
                    {
                        changedPMProjects.Add(new Employee
                        {
                            EmployeeId = employeeId,
                            DisplayedName = DisplayName,
                            LastName = LastName,
                            FirstName = FirstName,
                            Status = Status
                        });
                    }
                    using (var writer = new StringWriter())
                    using (var csvrow = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csvrow.WriteField(excelHelper.RowToCsv(row));   // Write array as a single row
                        csvrow.NextRecord();
                        string csvString = writer.ToString();
                        skipped++;
                        skippedRecords.Add(writer.ToString());
                        continue;
                    }
                }

                // ✅ Add new employee
                var Employee = new Employee
                {
                    EmployeeId = employeeId,
                    DisplayedName = DisplayName,
                    LastName = LastName,
                    FirstName = FirstName,
                    Status = Status,
                    CreatedBy = Username
                };

                Employees.Add(Employee);
                imported++;
            }
        }
        // ✅ Bulk insert for new projects
        if (Employees.Any())
        {
            await _context.BulkInsertAsync(Employees);
            await _context.SaveChangesAsync();
        }

        // ✅ Return skipped records as downloadable CSV
        if (skippedRecords.Any())
        {
            var csvContent = string.Join(Environment.NewLine, skippedRecords);
            var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
            return File(bytes, "text/csv", "SkippedEmployees.csv");
        }

        return Ok(new { imported, skipped, changedPMCount = changedPMProjects.Count, message = $"{imported} imported, {skipped} skipped, {changedPMProjects.Count} PMs changed." });
    }



    [HttpPost("import-employees-csv-s3")]
    public async Task<IActionResult> ImportEmployeesExcelS3Async(string filename, string Username)
    {
        var Employees = new List<Employee>();
        var skippedRecords = new List<string>();
        var changedPMProjects = new List<Employee>();  // ✅ Track PM changes
        int imported = 0, skipped = 0;

        // Cache existing projects for fast lookups
        var existingEmployees = await _context.Employees
            .Select(p => new { p.EmployeeId, p.DisplayedName, p.FirstName, p.LastName })
            .ToDictionaryAsync(p => p.EmployeeId, p => p);


        // Create S3 client with credentials and region
        var credentials = new BasicAWSCredentials(AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY);
        var s3Client = new AmazonS3Client(credentials, Amazon.RegionEndpoint.GetBySystemName(Region));

        // 1️⃣ Download CSV file
        Console.WriteLine($"📥 Downloading {filename} from S3 bucket: {BucketName}...");
        string csvText;
        using (var response = await s3Client.GetObjectAsync(BucketName, filename))
        using (var reader = new StreamReader(response.ResponseStream))
        {

            int row = 0;
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            });

            while (csv.Read())
            {
                var cols = csv.Parser.Record;

                var employeeId = cols.ElementAtOrDefault(0)?.Trim();
                var DisplayName = cols.ElementAtOrDefault(1)?.Trim();
                var LastName = cols.ElementAtOrDefault(2)?.Trim();
                var FirstName = cols.ElementAtOrDefault(3)?.Trim();
                var Status = cols.ElementAtOrDefault(6)?.Trim();

                if (string.IsNullOrEmpty(employeeId))
                {
                    using (var writer = new StringWriter())
                    using (var csvrow = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csvrow.WriteField(cols);   // Write array as a single row
                        csvrow.NextRecord();
                        string csvString = writer.ToString();
                        skipped++;
                        skippedRecords.Add(writer.ToString());
                        continue;
                    }
                }

                // ✅ Check if project exists
                if (existingEmployees.TryGetValue(employeeId, out var existing))
                {
                    // ✅ Detect PM change
                    if (!string.Equals(existing.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))
                    {
                        changedPMProjects.Add(new Employee
                        {
                            EmployeeId = employeeId,
                            DisplayedName = DisplayName,
                            LastName = LastName,
                            FirstName = FirstName,
                            Status = Status
                        });
                    }
                    using (var writer = new StringWriter())
                    using (var csvrow = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csvrow.WriteField(cols);   // Write array as a single row
                        csvrow.NextRecord();
                        string csvString = writer.ToString();
                        skipped++;
                        skippedRecords.Add(writer.ToString());
                        continue;
                    }
                }

                // ✅ Add new employee
                var Employee = new Employee
                {
                    EmployeeId = employeeId,
                    DisplayedName = DisplayName,
                    LastName = LastName,
                    FirstName = FirstName,
                    Status = Status,
                    CreatedBy = Username
                };

                Employees.Add(Employee);
                imported++;
            }
        }
        // ✅ Bulk insert for new projects
        if (Employees.Any())
        {
            await _context.BulkInsertAsync(Employees);
            await _context.SaveChangesAsync();
        }

        // ✅ Return skipped records as downloadable CSV
        if (skippedRecords.Any())
        {
            var csvContent = string.Join(Environment.NewLine, skippedRecords);
            var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
            return File(bytes, "text/csv", "SkippedEmployees.csv");
        }

        return Ok(new { imported, skipped, changedPMCount = changedPMProjects.Count, message = $"{imported} imported, {skipped} skipped, {changedPMProjects.Count} PMs changed." });
    }



    [HttpGet("export-csv")]
    public async Task<IActionResult> ExportTimesheetCsv()
    {
        //var timesheets = await _context.Timesheets.ToListAsync();
        string status = "APPROVED";
        // base query
        var approvalQuery = _context.ApprovalRequests.Where(p => p.Status == status && p.IsExported == false);

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
                        Comment = ar.Actions
                                     .Select(a => a.ActionComment)
                                     .FirstOrDefault(),
                        IPAddress = ar.Actions
                                     .Select(a => a.IpAddress)
                                     .FirstOrDefault()
                    };


        var result = await query
            .OrderBy(x => x.Timesheet.TimesheetDate ?? DateOnly.MaxValue)
            .ToListAsync();

        // map back into Timesheet objects
        var timesheets = result.Select(x =>
        {
            x.Timesheet.RequestId = x.RequestId;
            x.Timesheet.ApprovalStatus = x.ApprovalStatus;
            x.Timesheet.DisplayedName = x.EmployeeDisplayName;
            x.Timesheet.Comment = x.Comment;
            x.Timesheet.IPAddress = x.IPAddress;
            x.Timesheet.ApprovedBy = x.UserName;
            return x.Timesheet;
        }).ToList();


        var sb = new StringBuilder();

        // Header row (add Comment and IpAddress at the end)
        //sb.AppendLine("TimesheetDate,EmployeeId,TimesheetTypeCode,WorkingState,FiscalYear,Period,Subperiod,CorrectingRefDate,PayType,GeneralLaborCategory,TimesheetLineTypeCode,LaborCostAmount,Hours,WorkersCompCode,LaborLocationCode,OrganizationId,AccountId,ProjectId,ProjectLaborCategory,ReferenceNumber1,ReferenceNumber2,OrganizationAbbreviation,ProjectAbbreviation,SequenceNumber,EffectiveBillingDate,ProjectAccountAbbrev,MultiStateCode,ReferenceSequenceNum,TimesheetLineDate,Notes,Comment,IpAddress,Status,UpdatedBy");

        foreach (var t in timesheets)
        {
            sb.AppendLine(string.Join(",", new string[]
            {
            t.TimesheetDate?.ToString("M/d/yyyy", CultureInfo.InvariantCulture) ?? "",
            t.EmployeeId ?? "",
            t.TimesheetTypeCode ?? "",
            t.WorkingState ?? "",
            t.FiscalYear.ToString(),
            t.Period.ToString(),
            t.Subperiod?.ToString() ?? "",
            t.CorrectingRefDate?.ToString("M/d/yyyy") ?? "",
            t.PayType ?? "",
            t.GeneralLaborCategory ?? "",
            t.TimesheetLineTypeCode ?? "",
            t.LaborCostAmount?.ToString() ?? "",
            t.Hours?.ToString("0.##") ?? "",
            t.WorkersCompCode ?? "",
            t.LaborLocationCode ?? "",
            t.OrganizationId ?? "",
            t.AccountId ?? "",
            t.ProjectId ?? "",
            t.ProjectLaborCategory ?? "",
            t.ReferenceNumber1 ?? "",
            t.ReferenceNumber2 ?? "",
            t.OrganizationAbbreviation ?? "",
            t.ProjectAbbreviation ?? "",
            t.SequenceNumber?.ToString() ?? "",
            t.EffectiveBillingDate?.ToString("M/d/yyyy", CultureInfo.InvariantCulture) ?? "",
            t.ProjectAccountAbbrev ?? "",
            t.MultiStateCode ?? "",
            t.ReferenceSequenceNum?.ToString() ?? "",
            t.TimesheetLineDate?.ToString("M/d/yyyy", CultureInfo.InvariantCulture) ?? "",
            t.Notes ?? ""
            //t.Comment ?? "",
            //t.IPAddress ?? "",
            //t.ApprovalStatus ?? "",
            //t.ApprovedBy ?? "",
            }.Select(v => v.Replace(",", " ")))); // replace commas inside values
        }

        var timesheetids = timesheets.Select(p => p.TimesheetId);
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());

        var output = new MemoryStream(bytes);
        var count = await _helper.MarkAsExportedAsync(timesheetids.ToList());
        return File(output, "text/csv", "Employees.csv");
    }


    [HttpPost("export-csv")]
    public async Task<IActionResult> ExportTimesheetCsv(List<Timesheet> timesheets)
    {
        var sb = new StringBuilder();

        foreach (var t in timesheets)
        {
            sb.AppendLine(string.Join(",", new string[]
            {
            t.TimesheetDate?.ToString("M/d/yyyy") ?? "",
            t.EmployeeId ?? "",
            t.TimesheetTypeCode ?? "",
            t.WorkingState ?? "",
            t.FiscalYear.ToString(),
            t.Period.ToString(),
            t.Subperiod?.ToString() ?? "",
            t.CorrectingRefDate?.ToString("M/d/yyyy") ?? "",
            t.PayType ?? "",
            t.GeneralLaborCategory ?? "",
            t.TimesheetLineTypeCode ?? "",
            t.LaborCostAmount?.ToString() ?? "",
            t.Hours?.ToString() ?? "",
            t.WorkersCompCode ?? "",
            t.LaborLocationCode ?? "",
            t.OrganizationId ?? "",
            t.AccountId ?? "",
            t.ProjectId ?? "",
            t.ProjectLaborCategory ?? "",
            t.ReferenceNumber1 ?? "",
            t.ReferenceNumber2 ?? "",
            t.OrganizationAbbreviation ?? "",
            t.ProjectAbbreviation ?? "",
            t.SequenceNumber?.ToString() ?? "",
            t.EffectiveBillingDate?.ToString("M/d/yyyy") ?? "",
            t.ProjectAccountAbbrev ?? "",
            t.MultiStateCode ?? "",
            t.ReferenceSequenceNum?.ToString() ?? "",
            t.TimesheetLineDate?.ToString("M/d/yyyy") ?? ""
            //t.Notes ?? "",
            //t.Comment ?? "",
            //t.IPAddress ?? "",
            //t.ApprovalStatus ?? "",
            //t.ApprovedBy ?? "",
            }.Select(v => v.Replace(",", " ")))); // replace commas inside values
        }

        var timesheetids = timesheets.Select(p => p.TimesheetId).ToList();

        //////////////////////////////////////////////////////////////////////////
        ///
        try
        {
            var credentials = new BasicAWSCredentials(AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY);
            var s3Client = new AmazonS3Client(credentials, Amazon.RegionEndpoint.GetBySystemName(Region));
            var fileName = $"{ExportPath}/TS_Export_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));

            var uploadRequest = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = fileName,
                InputStream = stream,
                ContentType = "text/csv"
            };

            await s3Client.PutObjectAsync(uploadRequest);
        }
        catch (Exception ex)
        {

        }


        /////////////////////////////////////////////////////////////////////////

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var output = new MemoryStream(bytes);

        var count = await _helper.MarkAsExportedAsync(timesheetids.ToList());

        return File(output, "text/csv", "Employees.csv");
    }


    //public async Task<IActionResult> MarkAsExported([FromBody] List<int> timesheetIds)
    //{
    //    if (timesheetIds == null || !timesheetIds.Any())
    //        return BadRequest("No TimesheetIds provided.");

    //    var requests = await _context.ApprovalRequests
    //        .Where(r => timesheetIds.Contains(r.TimesheetId))
    //        .ToListAsync();

    //    foreach (var req in requests)
    //    {
    //        req.Status = "Exported";
    //    }

    //    await _context.SaveChangesAsync();

    //    return Ok(new { updated = requests.Count });
    //}

    /// <summary>
    /// Helper to safely parse date strings.
    /// </summary>
    //private DateTime? TryParseDate(string input)
    //{
    //    if (DateTime.TryParse(input, out var result))
    //        return result;
    //    return null;
    //}


    [HttpGet("GetAllTimesheets")]
    public async Task<IActionResult> GetAllTimesheets()
    {
        var records = await _context.Timesheets
            .OrderBy(t => t.TimesheetDate)
            .ToListAsync();

        return Ok(records);
    }

    [HttpGet("pending-approvals")]
    public async Task<IActionResult> GetTimesheetsNotInApprovalRequest()
    {
        TimeZoneInfo easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        var timesheets = await (from t in _context.Timesheets
                                join e in _context.Employees
                                    on t.EmployeeId equals e.EmployeeId
                                join pr in _context.Projects on t.ProjectId equals pr.ProjectId
                                where !_context.ApprovalRequests
                                    .Any(ar => ar.TimesheetId == t.TimesheetId)
                                orderby t.TimesheetDate
                                select new
                                {
                                    Timesheet = t,
                                    DisplayName = e.DisplayedName,
                                    ApprovedBy = pr.ProjectManagerName + " (" + pr.ProjectManagerId + ")"
                                })
                                .ToListAsync();

        var result = timesheets.Select(x =>
        {
            x.Timesheet.ImportedTimestamp = TimeZoneInfo.ConvertTimeFromUtc(x.Timesheet.CreatedDate, easternZone).ToString("MM/dd/yyyy hh:mm tt");
            x.Timesheet.DisplayedName = x.DisplayName;
            x.Timesheet.ApprovedBy = x.ApprovedBy;
            return x.Timesheet;
        }).ToList();
        result.ForEach(ts => ts.Status = "OPEN");

        //var Notifiedtimesheets = (
        //    from t in _context.Timesheets
        //    join e in _context.Employees on t.EmployeeId equals e.EmployeeId
        //    join ar in _context.ApprovalRequests on t.TimesheetId equals ar.TimesheetId
        //    join pr in _context.Projects on t.ProjectId equals pr.ProjectId
        //    where !ar.IsExported && ar.Actions.Count() == 0
        //    select new
        //    {
        //        t,
        //        e.DisplayedName,
        //        ar.Status,
        //        pr.ProjectManagerName,
        //        pr.ProjectManagerId,
        //        ar.IsExported,
        //        ar.Actions,
        //        ar.RequestId,
        //    }
        //).ToList();


        var currentDate = DateTime.UtcNow;

        var Notifiedtimesheets = (
            from t in _context.Timesheets
            join e in _context.Employees on t.EmployeeId equals e.EmployeeId
            join ar in _context.ApprovalRequests on t.TimesheetId equals ar.TimesheetId
            join pr in _context.Projects on t.ProjectId equals pr.ProjectId
            where !ar.IsExported
                  && (ar.Status.ToUpper() == "REJECTED" || ar.Status.ToUpper() == "PENDING")
            select new
            {
                t,
                e.DisplayedName,
                ar.Status,
                pr.ProjectManagerName,
                pr.ProjectManagerId,
                ar.IsExported,
                ar.Actions,
                ar.RequestId,
            }
        ).ToList();


        // update timesheet statuses
        foreach (var item in Notifiedtimesheets)
        {

            var ApprovedDate = item.Actions
                .Where(a => a.ActionDate != null)
                .OrderByDescending(a => a.ActionDate)
                .Select(a => a.ActionDate)
                .FirstOrDefault();

            if (ApprovedDate != null && ApprovedDate != DateTime.MinValue)
            {
                item.t.ApprovedDate = TimeZoneInfo.ConvertTimeFromUtc(
                    ApprovedDate,
                    easternZone
                ).ToString("MM/dd/yyyy hh:mm tt");
            }
            else
            {
                item.t.ApprovedDate = string.Empty;
            }
            item.t.ImportedTimestamp = TimeZoneInfo.ConvertTimeFromUtc(item.t.CreatedDate, easternZone).ToString("MM/dd/yyyy hh:mm tt");
            item.t.Status = item.Status;  // set from ApprovalRequests.Status
            item.t.ApprovedBy = item.ProjectManagerName + " (" + item.ProjectManagerId + ")";
            item.t.IsExported = item.IsExported;
            item.t.DisplayedName = item.DisplayedName;
            item.t.RequestId = item.RequestId;
            //item.t.ApprovedDate = TimeZoneInfo.ConvertTimeFromUtc(ApprovedDate, easternZone).ToString("MM/dd/yyyy hh:mm tt");
        }

        // populate the NotMapped property
        var NotifiedResult = Notifiedtimesheets.Select(x =>
        {
            return x.t;
        }).ToList();

        //return Ok(result);
        return Ok(result.Concat(NotifiedResult));
    }

    [HttpGet("pending-approvalsByUser")]
    public async Task<IActionResult> GetTimesheetsByUserAndStatus(
    string userName,
    string status,
    int? month,
    int? year)
    {
        // Default month/year -> current if not provided
        //int filterMonth = month ?? DateTime.Now.Month;
        //int filterYear = year ?? DateTime.Now.Year;

        // 1) Resolve user
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == userName);
        var userId = user?.UserId ?? 0;
        if (userId == 0)
            return NotFound("User not found");

        var userDetails = await _helper.GetUserDetails(userName);
        var role = user?.Role?.ToUpperInvariant();

        // 2) Build approvalQuery and apply role+level filters (extracted helper)
        IQueryable<ApprovalRequest> approvalQuery = _context.ApprovalRequests.Where(ar => !ar.IsExported);

        approvalQuery = ApplyRoleLevelFilter(approvalQuery, role, userId, userDetails.LevelNo.GetValueOrDefault());

        if (!string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var statusUpper = status.ToUpperInvariant();
            approvalQuery = approvalQuery.Where(ar => ar.Status.ToUpper() == statusUpper);
        }

        // 3) Timesheet base query with month/year filter

        IQueryable<Timesheet> timesheetQuery = _context.Timesheets
                .Include(t => t.ApprovalRequests)
                .Where(t =>
                    // CASE 1: PENDING requests (special handling)
                    t.ApprovalRequests.Any(ar =>
                        ar.Status.ToUpper() == "PENDING" &&
                        (role.ToUpper() == "ADMIN" || ar.CurrentLevelNo >= userDetails.LevelNo)
                    )
                    ||
                    // CASE 2: NON-PENDING requests (apply month/year filter)
                    (
                        !t.ApprovalRequests.Any(ar => ar.Status.ToUpper() == "PENDING")
                    //&&
                    //t.Period == filterMonth &&
                    //t.FiscalYear == filterYear
                    )
                );

        if (month.HasValue)
        {
            timesheetQuery = timesheetQuery.Where(x =>
                x.Period == month.Value
            );
        }
        if (year.HasValue)
        {
            timesheetQuery = timesheetQuery.Where(x => x.FiscalYear == year.Value
            );
        }
        //if (month.HasValue)
        //{
        //    timesheetQuery = timesheetQuery.Where(x =>
        //        x.TimesheetDate.Value.Month == month.Value
        //    );
        //}
        //if (year.HasValue)
        //{
        //    timesheetQuery = timesheetQuery.Where(x => x.TimesheetDate.Value.Year == year.Value
        //    );
        //}

        //var timesheetQuery;

        //if (role.ToUpper() != "ADMIN")
        //{
        //    timesheetQuery = _context.Timesheets.Include(p => p.ApprovalRequests)
        //            .Where(t =>
        //                // Always include PENDING status
        //                (t.ApprovalRequests.FirstOrDefault().Status.ToUpper() == "PENDING" && t.ApprovalRequests.FirstOrDefault().CurrentLevelNo == userDetails.LevelNo) ||

        //                // For all other statuses, apply Month & Year filter
        //                (t.Period == filterMonth &&
        //                 t.FiscalYear == filterYear)
        //            );
        //}
        //else
        //{
        //    timesheetQuery = _context.Timesheets.Include(p => p.ApprovalRequests)
        //            .Where(t =>
        //                // Always include PENDING status
        //                (t.ApprovalRequests.FirstOrDefault().Status.ToUpper() == "PENDING") ||

        //                // For all other statuses, apply Month & Year filter
        //                (t.Period == filterMonth &&
        //                 t.FiscalYear == filterYear)
        //            );
        //}



        //var timesheetQuery = _context.Timesheets
        //    .Where(t => t.TimesheetDate.HasValue &&
        //                t.TimesheetDate.Value.Year == filterYear &&
        //                t.TimesheetDate.Value.Month == filterMonth);

        // 4) Main query
        var query =
                from t in timesheetQuery
                join ar in approvalQuery on t.TimesheetId equals ar.TimesheetId
                join e in _context.Employees on t.EmployeeId equals e.EmployeeId
                join pr in _context.Projects on t.ProjectId equals pr.ProjectId
                select new
                {
                    Timesheet = t,
                    RequestId = ar.RequestId,
                    ApprovalStatus = ar.Status,
                    EmployeeDisplayName = e.DisplayedName,
                    Actions = ar.Actions,
                    IPAddress = ar.Actions.Select(a => a.IpAddress).FirstOrDefault(),
                    ApprovedBy = (pr.ProjectManagerName ?? "") + " (" + (pr.ProjectManagerId ?? "") + ")"
                };

        var results = await query
            .OrderBy(x => x.Timesheet.TimesheetDate ?? DateOnly.MaxValue)
            .ToListAsync();

        // 5) Timezone conversion helper
        TimeZoneInfo easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        // 6) Map and post-process
        foreach (var row in results)
        {
            // Get most recent approval action (if any)
            var lastAction = row.Actions
                .Where(a => a.ActionDate != null)
                .OrderByDescending(a => a.ActionDate)
                .FirstOrDefault();

            if (lastAction != null && lastAction.ActionDate != DateTime.MinValue)
            {
                row.Timesheet.ApprovedDate = TimeZoneInfo.ConvertTimeFromUtc(
                    lastAction.ActionDate,
                    easternZone
                ).ToString("MM/dd/yyyy hh:mm tt");

                row.Timesheet.Comment = lastAction.ActionComment;
                row.Timesheet.ApprovalStatus = lastAction.ActionStatus;
            }
            else
            {
                row.Timesheet.ApprovedDate = string.Empty;
            }

            row.Timesheet.RequestId = row.RequestId;
            row.Timesheet.ApprovalActions = row.Actions;
            row.Timesheet.ImportedTimestamp = TimeZoneInfo.ConvertTimeFromUtc(row.Timesheet.CreatedDate, easternZone)
                .ToString("MM/dd/yyyy hh:mm tt");
            row.Timesheet.DisplayedName = row.EmployeeDisplayName;
            row.Timesheet.IPAddress = row.IPAddress;
            row.Timesheet.ApprovedBy = row.ApprovedBy;
            //row.Timesheet.ApprovalActions = row.Actions;

            // If user is USER/BACKUPUSER, prefer the action status for the user's level
            if (role == "USER" || role == "BACKUPUSER")
            {
                row.Timesheet.ApprovalStatus =
                    row.Actions.FirstOrDefault(a => a.LevelNo == userDetails.LevelNo)?.ActionStatus
                    ?? row.ApprovalStatus;
            }
            else
            {
                row.Timesheet.ApprovalStatus = row.ApprovalStatus;
            }
        }

        return Ok(results.Select(r => r.Timesheet).ToList());


        // -------------------------
        // Local helper: applies role+level filtering to approvalQuery
        // -------------------------
        IQueryable<ApprovalRequest> ApplyRoleLevelFilter(
            IQueryable<ApprovalRequest> q, string roleUpper, int uid, int levelNo)
        {
            if (roleUpper == "USER" || roleUpper == "BACKUPUSER")
            {
                return levelNo switch
                {
                    4 => q.Where(ar => ar.CurrentLevelNo >= 1),
                    1 => q.Where(ar => ar.RequesterId == uid && ar.CurrentLevelNo >= 1),
                    2 => q.Where(ar => ar.CurrentLevelNo >= 2),
                    _ => q
                };
            }
            return q;
        }
    }


    [HttpGet("pending-approvalsByUserV1")]
    public async Task<IActionResult> GetTimesheetsByUserAndStatusV1(
string userName,
string status,
int? month,
int? year)
    {
        // Default month/year -> current if not provided
        //int filterMonth = month ?? DateTime.Now.Month;
        //int filterYear = year ?? DateTime.Now.Year;

        // 1) Resolve user
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == userName);
        var userId = user?.UserId ?? 0;
        if (userId == 0)
            return NotFound("User not found");

        // Get all users for whom this user is a backup
        var backupForUsers = await _context.UserBackups
            .Where(ub => ub.BackupUserId == user.UserId)
            .Select(ub => ub.UserId)
            .ToListAsync();

        // Include self
        var relevantUserIds = backupForUsers.Append(userId).ToList();
        /////////////////////////////////////////////////////////////////////////////

        var userDetails = await _helper.GetUserDetails(userName);
        var role = user?.Role?.ToUpperInvariant();

        // 2) Build approvalQuery and apply role+level filters (extracted helper)
        IQueryable<ApprovalRequest> approvalQuery = _context.ApprovalRequests.Where(ar => !ar.IsExported);

        //approvalQuery = ApplyRoleLevelFilter(approvalQuery, role, userId, userDetails.LevelNo.GetValueOrDefault());
        approvalQuery = ApplyRoleLevelFilterV1(
            approvalQuery,
            role,
            relevantUserIds,
            userDetails.LevelNo.GetValueOrDefault()
        );

        if (!string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var statusUpper = status.ToUpperInvariant();
            approvalQuery = approvalQuery.Where(ar => ar.Status.ToUpper() == statusUpper);
        }

        // 3) Timesheet base query with month/year filter

        IQueryable<Timesheet> timesheetQuery = _context.Timesheets
                .Include(t => t.ApprovalRequests)
                .Where(t =>
                    // CASE 1: PENDING requests (special handling)
                    t.ApprovalRequests.Any(ar =>
                        ar.Status.ToUpper() == "PENDING" &&
                        (role.ToUpper() == "ADMIN" || ar.CurrentLevelNo >= userDetails.LevelNo)
                    )
                    ||
                    // CASE 2: NON-PENDING requests (apply month/year filter)
                    (
                        !t.ApprovalRequests.Any(ar => ar.Status.ToUpper() == "PENDING")
                    )
                );

        if (month.HasValue)
        {
            timesheetQuery = timesheetQuery.Where(x =>
                x.Period == month.Value
            );
        }
        if (year.HasValue)
        {
            timesheetQuery = timesheetQuery.Where(x => x.FiscalYear == year.Value
            );
        }

        // 4) Main query
        var query =
                from t in timesheetQuery
                join ar in approvalQuery on t.TimesheetId equals ar.TimesheetId
                join e in _context.Employees on t.EmployeeId equals e.EmployeeId
                join pr in _context.Projects on t.ProjectId equals pr.ProjectId
                select new
                {
                    Timesheet = t,
                    RequestId = ar.RequestId,
                    ApprovalStatus = ar.Status,
                    EmployeeDisplayName = e.DisplayedName,
                    Actions = ar.Actions,
                    IPAddress = ar.Actions.Select(a => a.IpAddress).FirstOrDefault(),
                    ApprovedBy = (pr.ProjectManagerName ?? "") + " (" + (pr.ProjectManagerId ?? "") + ")",
                    ApproverId = pr.ProjectManagerId
                };

        var results = await query
            .OrderBy(x => x.Timesheet.TimesheetDate ?? DateOnly.MaxValue)
            .ToListAsync();

        // 5) Timezone conversion helper
        TimeZoneInfo easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        // 6) Map and post-process
        foreach (var row in results)
        {
            // Get most recent approval action (if any)
            var lastAction = row.Actions
                .Where(a => a.ActionDate != null)
                .OrderByDescending(a => a.ActionDate)
                .FirstOrDefault();

            if (lastAction != null && lastAction.ActionDate != DateTime.MinValue)
            {
                row.Timesheet.ApprovedDate = TimeZoneInfo.ConvertTimeFromUtc(
                    lastAction.ActionDate,
                    easternZone
                ).ToString("MM/dd/yyyy hh:mm tt");

                row.Timesheet.Comment = lastAction.ActionComment;
                row.Timesheet.ApprovalStatus = lastAction.ActionStatus;
            }
            else
            {
                row.Timesheet.ApprovedDate = string.Empty;
            }

            row.Timesheet.RequestId = row.RequestId;
            row.Timesheet.ApprovalActions = row.Actions;
            row.Timesheet.ImportedTimestamp = TimeZoneInfo.ConvertTimeFromUtc(row.Timesheet.CreatedDate, easternZone)
                .ToString("MM/dd/yyyy hh:mm tt");
            row.Timesheet.DisplayedName = row.EmployeeDisplayName;
            row.Timesheet.IPAddress = row.IPAddress;
            row.Timesheet.ApprovedBy = row.ApprovedBy;
            row.Timesheet.ApproverId = row.ApproverId;
            //row.Timesheet.ApprovalActions = row.Actions;

            // If user is USER/BACKUPUSER, prefer the action status for the user's level
            if (role == "USER" || role == "BACKUPUSER")
            {
                row.Timesheet.ApprovalStatus =
                    row.Actions.FirstOrDefault(a => a.LevelNo == userDetails.LevelNo)?.ActionStatus
                    ?? row.ApprovalStatus;
            }
            else
            {
                row.Timesheet.ApprovalStatus = row.ApprovalStatus;
            }
        }

        return Ok(results.Select(r => r.Timesheet).ToList());

        // -------------------------
        // Local helper: applies role+level filtering to approvalQuery
        // -------------------------
        IQueryable<ApprovalRequest> ApplyRoleLevelFilterV1(
            IQueryable<ApprovalRequest> q, string roleUpper, List<int> userIds, int levelNo)
        {
            if (roleUpper == "USER" || roleUpper == "BACKUPUSER")
            {
                return levelNo switch
                {
                    4 => q.Where(ar => ar.CurrentLevelNo >= 1),
                    1 => q.Where(ar => userIds.Contains(ar.RequesterId) && ar.CurrentLevelNo >= 1),
                    2 => q.Where(ar => ar.CurrentLevelNo >= 2),
                    _ => q
                };
            }
            return q;
        }
    }




    [HttpGet("GetExportedTimesheetsByUser")]
    public async Task<IActionResult> GetExportedTimesheetsByUser(
    string userName,
    string status,
    int? month,
    int? year)
    {
        // Default month/year -> current if not provided
        //int filterMonth = month ?? DateTime.Now.Month;
        //int filterYear = year ?? DateTime.Now.Year;

        // 1) Resolve user
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == userName);
        var userId = user?.UserId ?? 0;
        if (userId == 0)
            return NotFound("User not found");

        var userDetails = await _helper.GetUserDetails(userName);
        var role = user?.Role?.ToUpperInvariant();

        // 2) Build approvalQuery and apply role+level filters (extracted helper)
        IQueryable<ApprovalRequest> approvalQuery = _context.ApprovalRequests.Where(ar => ar.IsExported);

        approvalQuery = ApplyRoleLevelFilter(approvalQuery, role, userId, userDetails.LevelNo.GetValueOrDefault());

        //if (!string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
        //{
        //    var statusUpper = status.ToUpperInvariant();
        //    approvalQuery = approvalQuery.Where(ar => ar.Status.ToUpper() == statusUpper);
        //}

        // 3) Timesheet base query with month/year filter

        IQueryable<Timesheet> timesheetQuery = _context.Timesheets
                .Include(t => t.ApprovalRequests)
                .Where(t =>
                    // CASE 1: PENDING requests (special handling)
                    t.ApprovalRequests.Any(ar =>
                        ar.Status.ToUpper() == "APPROVED" &&
                        (role.ToUpper() == "ADMIN" || ar.CurrentLevelNo >= userDetails.LevelNo)
                    )
                    ||
                    // CASE 2: NON-PENDING requests (apply month/year filter)
                    (
                        !t.ApprovalRequests.Any(ar => ar.Status.ToUpper() == "APPROVED")
                    )
                );

        if (month.HasValue)
        {
            timesheetQuery = timesheetQuery.Where(x =>
                x.Period == month.Value
            );
        }
        if (year.HasValue)
        {
            timesheetQuery = timesheetQuery.Where(x => x.FiscalYear == year.Value
            );
        }

        // 4) Main query
        var query =
                from t in timesheetQuery
                join ar in approvalQuery on t.TimesheetId equals ar.TimesheetId
                join e in _context.Employees on t.EmployeeId equals e.EmployeeId
                join pr in _context.Projects on t.ProjectId equals pr.ProjectId
                select new
                {
                    Timesheet = t,
                    RequestId = ar.RequestId,
                    ApprovalStatus = ar.Status,
                    EmployeeDisplayName = e.DisplayedName,
                    Actions = ar.Actions,
                    IPAddress = ar.Actions.Select(a => a.IpAddress).FirstOrDefault(),
                    ApprovedBy = (pr.ProjectManagerName ?? "") + " (" + (pr.ProjectManagerId ?? "") + ")"
                };

        var results = await query
            .OrderBy(x => x.Timesheet.TimesheetDate ?? DateOnly.MaxValue)
            .ToListAsync();

        // 5) Timezone conversion helper
        TimeZoneInfo easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        // 6) Map and post-process
        foreach (var row in results)
        {
            // Get most recent approval action (if any)
            var lastAction = row.Actions
                .Where(a => a.ActionDate != null)
                .OrderByDescending(a => a.ActionDate)
                .FirstOrDefault();

            if (lastAction != null && lastAction.ActionDate != DateTime.MinValue)
            {
                row.Timesheet.ApprovedDate = TimeZoneInfo.ConvertTimeFromUtc(
                    lastAction.ActionDate,
                    easternZone
                ).ToString("MM/dd/yyyy hh:mm tt");

                row.Timesheet.Comment = lastAction.ActionComment;
                row.Timesheet.ApprovalStatus = lastAction.ActionStatus;
            }
            else
            {
                row.Timesheet.ApprovedDate = string.Empty;
            }

            row.Timesheet.RequestId = row.RequestId;
            row.Timesheet.ApprovalActions = row.Actions;
            row.Timesheet.ImportedTimestamp = TimeZoneInfo.ConvertTimeFromUtc(row.Timesheet.CreatedDate, easternZone)
                .ToString("MM/dd/yyyy hh:mm tt");
            row.Timesheet.DisplayedName = row.EmployeeDisplayName;
            row.Timesheet.IPAddress = row.IPAddress;
            row.Timesheet.ApprovedBy = row.ApprovedBy;
            //row.Timesheet.ApprovalActions = row.Actions;

            // If user is USER/BACKUPUSER, prefer the action status for the user's level
            if (role == "USER" || role == "BACKUPUSER")
            {
                row.Timesheet.ApprovalStatus =
                    row.Actions.FirstOrDefault(a => a.LevelNo == userDetails.LevelNo)?.ActionStatus
                    ?? row.ApprovalStatus;
            }
            else
            {
                row.Timesheet.ApprovalStatus = row.ApprovalStatus;
            }
        }

        return Ok(results.Select(r => r.Timesheet).ToList());


        // -------------------------
        // Local helper: applies role+level filtering to approvalQuery
        // -------------------------
        IQueryable<ApprovalRequest> ApplyRoleLevelFilter(
            IQueryable<ApprovalRequest> q, string roleUpper, int uid, int levelNo)
        {
            if (roleUpper == "USER" || roleUpper == "BACKUPUSER")
            {
                return levelNo switch
                {
                    4 => q.Where(ar => ar.CurrentLevelNo >= 1),
                    1 => q.Where(ar => ar.RequesterId == uid && ar.CurrentLevelNo >= 1),
                    2 => q.Where(ar => ar.CurrentLevelNo >= 2),
                    _ => q
                };
            }
            return q;
        }
    }


    //[HttpGet("pending-approvalsByUser")]
    //public async Task<IActionResult> GetTimesheetsByUserAndStatus(string userName, string status)
    //{
    //    // get current user id
    //    var user = await _context.Users
    //        .Where(p => p.Username == userName)
    //        //.Select(p => p.UserId)
    //        .FirstOrDefaultAsync();

    //    var userId = user != null ? user.UserId : 0;

    //    if (userId == 0)
    //        return NotFound("User not found");

    //    var userDetails = await _helper.GetUserDetails(userName);

    //    // base query
    //    var approvalQuery = _context.ApprovalRequests
    //        .Where(ar => !ar.IsExported);

    //    if ((user?.Role.ToUpper() == "USER" || user?.Role.ToUpper() == "BACKUPUSER") && userDetails.LevelNo == 1)
    //    {
    //        approvalQuery = approvalQuery.Where(ar => ar.CurrentLevelNo >= 1);
    //    }
    //    else
    //    {
    //        if ((user?.Role.ToUpper() == "USER" || user?.Role.ToUpper() == "BACKUPUSER") && userDetails.LevelNo == 2)
    //        {
    //            approvalQuery = approvalQuery.Where(ar => ar.RequesterId == userId && ar.CurrentLevelNo >= 2);
    //        }
    //        else
    //        {
    //            if ((user?.Role.ToUpper() == "USER" || user?.Role.ToUpper() == "BACKUPUSER") && userDetails.LevelNo == 3)
    //            {
    //                approvalQuery = approvalQuery.Where(ar => ar.CurrentLevelNo >= 3);
    //            }
    //        }
    //    }

    //    //var role = user?.Role?.ToUpperInvariant();

    //    //switch (role)
    //    //{
    //    //    case "USER":
    //    //        approvalQuery = userDetails.LevelNo switch
    //    //        {
    //    //            1 => approvalQuery.Where(ar => ar.CurrentLevelNo >= 1),
    //    //            2 => approvalQuery.Where(ar => ar.RequesterId == userId && ar.CurrentLevelNo >= 2),
    //    //            3 => approvalQuery.Where(ar => ar.CurrentLevelNo >= 3),
    //    //            _ => approvalQuery
    //    //        };
    //    //        break;

    //    //    case "BACKUPUSER":
    //    //        approvalQuery = userDetails.LevelNo switch
    //    //        {
    //    //            1 => approvalQuery.Where(ar => ar.CurrentLevelNo >= 1),
    //    //            2 => approvalQuery.Where(ar => ar.RequesterId == userId && ar.CurrentLevelNo >= 2),
    //    //            3 => approvalQuery.Where(ar => ar.CurrentLevelNo >= 3),
    //    //            _ => approvalQuery
    //    //        };

    //    //        break;
    //    //}




    //    //{
    //    //    approvalQuery = approvalQuery.Where(ar => ar.Actions.Count() > 0);
    //    //}

    //    if (!string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
    //    {
    //        approvalQuery = approvalQuery.Where(ar => ar.Status.ToUpper() == status.ToUpper());
    //    }


    //    var query = from t in _context.Timesheets
    //                join ar in approvalQuery on t.TimesheetId equals ar.TimesheetId
    //                join e in _context.Employees on t.EmployeeId equals e.EmployeeId
    //                join pr in _context.Projects on t.ProjectId equals pr.ProjectId
    //                select new
    //                {
    //                    Timesheet = t,
    //                    RequestId = ar.RequestId,

    //                    ApprovalStatus = ar.Status,
    //                    EmployeeDisplayName = e.DisplayedName,
    //                    Comment = ar.Actions
    //                                 .Select(a => a.ActionComment)
    //                                 .FirstOrDefault(),
    //                    Actions = ar.Actions,
    //                    //ApprovedDate = ar.Actions
    //                    //             .Select(a => a.ActionDate)
    //                    //             .FirstOrDefault(),
    //                    IPAddress = ar.Actions
    //                                 .Select(a => a.IpAddress)
    //                                 .FirstOrDefault(),
    //                    ApprovedBy = pr.ProjectManagerName + " (" + pr.ProjectManagerId + ")"
    //                };


    //    var result = await query
    //        .OrderBy(x => x.Timesheet.TimesheetDate ?? DateOnly.MaxValue)
    //        .ToListAsync();
    //    TimeZoneInfo easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    //    // map back into Timesheet objects

    //    foreach (var item in result)
    //    {

    //        var ApprovedDate = item.Actions
    //            .Where(a => a.ActionDate != null)
    //            .OrderByDescending(a => a.ActionDate)
    //            //.Select(a => a.ActionDate)
    //            .FirstOrDefault();

    //        if (ApprovedDate != null && ApprovedDate.ActionDate != DateTime.MinValue)
    //        {
    //            item.Timesheet.ApprovedDate = TimeZoneInfo.ConvertTimeFromUtc(
    //                ApprovedDate.ActionDate,
    //                easternZone
    //            ).ToString("MM/dd/yyyy hh:mm tt");

    //            item.Timesheet.Comment = ApprovedDate.ActionComment;
    //            item.Timesheet.ApprovalStatus = ApprovedDate.ActionStatus;

    //        }
    //        else
    //        {
    //            item.Timesheet.ApprovedDate = string.Empty;
    //        }
    //        item.Timesheet.RequestId = item.RequestId;
    //        item.Timesheet.ApprovalActions = item.Actions;
    //        item.Timesheet.ImportedTimestamp = TimeZoneInfo.ConvertTimeFromUtc(item.Timesheet.CreatedDate, easternZone).ToString("MM/dd/yyyy hh:mm tt");
    //        // set from ApprovalRequests.Status
    //        item.Timesheet.DisplayedName = item.EmployeeDisplayName;
    //        item.Timesheet.IPAddress = item.IPAddress;
    //        item.Timesheet.ApprovedBy = item.ApprovedBy;
    //        if (item.RequestId == 2504)
    //        {

    //        }
    //        if (user?.Role.ToUpper() == "USER" || user?.Role.ToUpper() == "BACKUPUSER")
    //        {
    //            //item.Timesheet.ApprovalStatus = item.Actions.Count() > 0 ? item.Actions.FirstOrDefault(p=>p.LevelNo == userDetails.LevelNo)?.ActionStatus : item.ApprovalStatus;
    //            item.Timesheet.ApprovalStatus =
    //                item.Actions.FirstOrDefault(a => a.LevelNo == userDetails.LevelNo)?.ActionStatus
    //                ?? item.ApprovalStatus;

    //        }
    //        else
    //        {
    //            item.Timesheet.ApprovalStatus = item.ApprovalStatus;
    //        }

    //        //item.t.ApprovedDate = TimeZoneInfo.ConvertTimeFromUtc(ApprovedDate, easternZone).ToString("MM/dd/yyyy hh:mm tt");
    //    }
    //    return Ok(result.Select(p => p.Timesheet).ToList());
    //}


    [HttpGet("pending-approvalsByStatus")]
    public async Task<IActionResult> GetTimesheetsByStatus(string status)
    {
        // base query
        var approvalQuery = _context.ApprovalRequests
            .Where(ar => ar.Status.ToUpper() == status.ToUpper() && !ar.IsExported);

        var query = from t in _context.Timesheets
                    join pr in _context.Projects on t.ProjectId equals pr.ProjectId
                    join ar in approvalQuery on t.TimesheetId equals ar.TimesheetId
                    join e in _context.Employees on t.EmployeeId equals e.EmployeeId
                    select new
                    {
                        Timesheet = t,
                        RequestId = ar.RequestId,
                        ApproverName = pr.ProjectManagerName + " (" + pr.ProjectManagerId + ")",
                        ApprovalStatus = ar.Status,
                        IsExported = ar.IsExported,
                        EmployeeDisplayName = e.DisplayedName,
                        Comment = ar.Actions
                                     .Select(a => a.ActionComment)
                                     .FirstOrDefault(),
                        Actions = ar.Actions,
                        IPAddress = ar.Actions
                                     .Select(a => a.IpAddress)
                                     .FirstOrDefault()
                    };


        var result = await query
            .OrderBy(x => x.Timesheet.TimesheetDate ?? DateOnly.MaxValue)
            .ToListAsync();
        TimeZoneInfo easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        // map back into Timesheet objects
        //var timesheets = result.Select(x =>
        //{
        //    x.Timesheet.RequestId = x.RequestId;
        //    x.Timesheet.ApprovalStatus = x.ApprovalStatus;
        //    x.Timesheet.DisplayedName = x.EmployeeDisplayName;
        //    x.Timesheet.Comment = x.Comment;
        //    x.Timesheet.IPAddress = x.IPAddress;
        //    x.Timesheet.ApprovedBy = x.ApproverName;
        //    x.Timesheet.ApprovedDate = TimeZoneInfo.ConvertTimeFromUtc(x.ApprovedDate, easternZone).ToString("MM/dd/yyyy hh:mm tt");
        //    x.Timesheet.IsExported = x.IsExported;

        //    return x.Timesheet;
        //}).ToList();

        foreach (var x in result)
        {

            var ApprovedDate = x.Actions
                .Where(a => a.ActionDate != null)
                .OrderByDescending(a => a.ActionDate)
                .Select(a => a.ActionDate)
                .FirstOrDefault();

            if (ApprovedDate != null && ApprovedDate != DateTime.MinValue)
            {
                x.Timesheet.ApprovedDate = TimeZoneInfo.ConvertTimeFromUtc(
                    ApprovedDate,
                    easternZone
                ).ToString("MM/dd/yyyy hh:mm tt");
            }
            else
            {
                x.Timesheet.ApprovedDate = string.Empty;
            }
            x.Timesheet.ImportedTimestamp = TimeZoneInfo.ConvertTimeFromUtc(x.Timesheet.CreatedDate, easternZone).ToString("MM/dd/yyyy hh:mm tt");
            x.Timesheet.RequestId = x.RequestId;
            x.Timesheet.ApprovalStatus = x.ApprovalStatus;
            x.Timesheet.DisplayedName = x.EmployeeDisplayName;
            x.Timesheet.Comment = x.Comment;
            x.Timesheet.IPAddress = x.IPAddress;
            x.Timesheet.ApprovedBy = x.ApproverName;
            x.Timesheet.IsExported = x.IsExported;

        }

        return Ok(result.Select(P => P.Timesheet).ToList());
    }


    [HttpGet("GetExportedTimesheets")]
    public async Task<IActionResult> GetExportedTimesheets(int? month, int? year)
    {
        // base query
        var approvalQuery = _context.ApprovalRequests
            .Where(ar => ar.IsExported && ar.Status.ToUpper() == "APPROVED");

        var query = from t in _context.Timesheets
                    join pr in _context.Projects on t.ProjectId equals pr.ProjectId
                    join ar in approvalQuery on t.TimesheetId equals ar.TimesheetId
                    join e in _context.Employees on t.EmployeeId equals e.EmployeeId
                    select new
                    {
                        Timesheet = t,
                        RequestId = ar.RequestId,
                        ApproverName = pr.ProjectManagerName + " (" + pr.ProjectManagerId + ")",
                        ApprovalStatus = ar.Status,
                        IsExported = ar.IsExported,
                        EmployeeDisplayName = e.DisplayedName,
                        Comment = ar.Actions
                                     .Select(a => a.ActionComment)
                                     .FirstOrDefault(),
                        Actions = ar.Actions,
                        IPAddress = ar.Actions
                                     .Select(a => a.IpAddress)
                                     .FirstOrDefault()
                    };
        if (month.HasValue)
        {
            query = query.Where(x =>
                x.Timesheet.Period == month.Value
            );
        }
        if (year.HasValue)
        {
            query = query.Where(x => x.Timesheet.FiscalYear == year.Value
            );
        }

        var result = await query
            .OrderBy(x => x.Timesheet.TimesheetDate ?? DateOnly.MaxValue)
            .ToListAsync();
        TimeZoneInfo easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        foreach (var x in result)
        {

            var ApprovedDate = x.Actions
                .Where(a => a.ActionDate != null)
                .OrderByDescending(a => a.ActionDate)
                .Select(a => a.ActionDate)
                .FirstOrDefault();

            if (ApprovedDate != null && ApprovedDate != DateTime.MinValue)
            {
                x.Timesheet.ApprovedDate = TimeZoneInfo.ConvertTimeFromUtc(
                    ApprovedDate,
                    easternZone
                ).ToString("MM/dd/yyyy hh:mm tt");
            }
            else
            {
                x.Timesheet.ApprovedDate = string.Empty;
            }
            x.Timesheet.ImportedTimestamp = TimeZoneInfo.ConvertTimeFromUtc(x.Timesheet.CreatedDate, easternZone).ToString("MM/dd/yyyy hh:mm tt");
            x.Timesheet.RequestId = x.RequestId;
            x.Timesheet.ApprovalStatus = x.ApprovalStatus;
            x.Timesheet.DisplayedName = x.EmployeeDisplayName;
            x.Timesheet.Comment = x.Comment;
            x.Timesheet.IPAddress = x.IPAddress;
            x.Timesheet.ApprovedBy = x.ApproverName;
            x.Timesheet.IsExported = x.IsExported;

        }

        return Ok(result.Select(P => P.Timesheet).ToList());
    }

    [HttpGet("GetAllTimesheetsPaged")]
    public async Task<IActionResult> GetTimesheetsPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 50;

        var query = _context.Timesheets.AsQueryable();

        var totalCount = await query.CountAsync();

        var records = await query
            .OrderBy(t => t.TimesheetDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            page,
            pageSize,
            totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            data = records
        });
    }

    private DateOnly? TryParseDate(ICell cell)
    {
        if (cell == null) return null;

        if (cell.CellType == CellType.Numeric && DateUtil.IsCellDateFormatted(cell))
            return cell.DateOnlyCellValue;

        if (DateOnly.TryParse(cell.ToString(), out var parsed))
            return parsed;

        return null;
    }

    //[HttpGet("GetApprovalStatuses")]
    //public async Task<IActionResult> GetApprovalStatuses(
    //[FromQuery] int? userId = null,
    //[FromQuery] int? approverId = null,
    //[FromQuery] DateTime? fromDate = null,
    //[FromQuery] DateTime? toDate = null)
    //{
    //    var query = _context.ApprovalRequests.AsQueryable();

    //    // Filter by creator (optional)
    //    if (userId.HasValue)
    //        query = query.Where(r => r.RequesterId == userId.Value);

    //    // Filter by date (optional)
    //    if (fromDate.HasValue)
    //        query = query.Where(r => r.Timesheet.TimesheetDate >= DateOnly.FromDateTime(fromDate.Value));
    //    if (toDate.HasValue)
    //        query = query.Where(r => r.Timesheet.TimesheetDate >= DateOnly.FromDateTime(toDate.Value));

    //    // Filter by pending approver
    //    if (approverId.HasValue)
    //    {
    //        // Include requests where approver is next to act
    //        query = from r in _context.ApprovalRequests
    //                join w in _context.ApprovalWorkflows on r.RequestType equals w.RequestType
    //                join a in _context.ApprovalApprovers on w.WorkflowId equals a.WorkflowId
    //                where a.UserId == approverId.Value && w.LevelNo == r.CurrentLevelNo
    //                select r;
    //    }

    //    var requests = await query.ToListAsync();
    //    var resultList = new List<ApprovalRequestStatusDto>();

    //    foreach (var request in requests)
    //    {
    //        var workflowLevels = await (
    //            from w in _context.ApprovalWorkflows
    //            where w.RequestType == request.RequestType
    //            orderby w.LevelNo
    //            select new { w.LevelNo, w.ApproverRole, w.WorkflowId }
    //        ).ToListAsync();

    //        var actions = await (
    //            from a in _context.ApprovalActions
    //            join u in _context.Users on a.ApproverId equals u.UserId
    //            where a.RequestId == request.RequestId
    //            select new { a.LevelNo, a.ActionStatus, a.ActionComment, a.ActionDate, Approver = u.FullName }
    //        ).ToListAsync();

    //        var levelDtos = workflowLevels
    //            .Select(w =>
    //            {
    //                var act = actions.FirstOrDefault(a => a.LevelNo == w.LevelNo);
    //                return new ApprovalLevelStatusDto
    //                {
    //                    LevelNo = w.LevelNo,
    //                    LevelName = w.ApproverRole,
    //                    Approver = act?.Approver ?? "(Pending Approver)",
    //                    Action = act?.ActionStatus ?? "PENDING",
    //                    Comment = act?.ActionComment,
    //                    ActionDate = act?.ActionDate
    //                };
    //            }).ToList();

    //        // ✅ Determine Next Approver(s)
    //        string? nextApprover = null;
    //        if (request.Status == "PENDING")
    //        {
    //            int nextLevel = request.CurrentLevelNo;
    //            var nextWorkflow = workflowLevels.FirstOrDefault(w => w.LevelNo == nextLevel);
    //            if (nextWorkflow != null)
    //            {
    //                var nextUsers = await (
    //                    from a in _context.ApprovalApprovers
    //                    join u in _context.Users on a.UserId equals u.UserId
    //                    where a.WorkflowId == nextWorkflow.WorkflowId
    //                    select u.FullName
    //                ).ToListAsync();

    //                if (nextUsers.Any())
    //                    nextApprover = $"{string.Join(", ", nextUsers)} ({nextWorkflow.ApproverRole})";
    //            }
    //        }

    //        resultList.Add(new ApprovalRequestStatusDto
    //        {
    //            RequestId = request.RequestId,
    //            RequestType = request.RequestType,
    //            OverallStatus = request.Status,
    //            CurrentLevelNo = request.CurrentLevelNo,
    //            NextApprovers = nextApprover,
    //            Levels = levelDtos
    //        });
    //    }

    //    return Ok(resultList);
    //}

    [HttpGet("GetApprovalStatusesNew")]
    public async Task<IActionResult> GetApprovalStatusesNew()
    {
        var requests = await _context.ApprovalRequests
            .Include(r => r.Requester)
            .ToListAsync();

        var resultList = new List<ApprovalRequestStatusDto>();

        foreach (var request in requests)
        {
            // Fetch all workflow levels for this request type
            var workflowLevels = await _context.ApprovalWorkflows
                .Where(w => w.RequestType == request.RequestType)
                .OrderBy(w => w.LevelNo)
                .ToListAsync();

            // Fetch all actions recorded for this request
            var actions = await (
                from a in _context.ApprovalActions
                join u in _context.Users on a.ApproverId equals u.UserId
                where a.RequestId == request.RequestId
                select new
                {
                    a.LevelNo,
                    a.ActionStatus,
                    a.ActionComment,
                    a.ActionDate,
                    ApproverName = u.FullName
                }
            ).ToListAsync();

            // Build per-level status
            var levelDtos = workflowLevels.Select(level =>
            {
                var action = actions.FirstOrDefault(a => a.LevelNo == level.LevelNo);
                return new ApprovalLevelStatusDto
                {
                    LevelNo = level.LevelNo,
                    LevelName = level.ApproverRole,
                    Approver = action?.ApproverName ?? "(Pending Approver)",
                    Action = action?.ActionStatus ?? "PENDING",
                    Comment = action?.ActionComment,
                    ActionDate = action?.ActionDate
                };
            }).ToList();

            // Identify rejection details (if any)
            var rejectedAction = actions.FirstOrDefault(a =>
                a.ActionComment.Equals("REJECTED", StringComparison.OrdinalIgnoreCase));

            RejectedInfoDto? rejectedBy = null;
            if (rejectedAction != null)
            {
                var rejectedLevel = workflowLevels.FirstOrDefault(l => l.LevelNo == rejectedAction.LevelNo);
                rejectedBy = new RejectedInfoDto
                {
                    Name = rejectedAction.ApproverName,
                    LevelName = rejectedLevel?.ApproverRole ?? $"Level {rejectedAction.LevelNo}",
                    Comment = rejectedAction.ActionComment,
                    ActionDate = rejectedAction.ActionDate
                };
            }

            // Determine next approvers for pending request
            List<ApproverInfoDto>? nextApprovers = null;
            if (request.Status == "PENDING")
            {
                int nextLevel = request.CurrentLevelNo;
                var nextWorkflow = workflowLevels.FirstOrDefault(w => w.LevelNo == nextLevel);

                if (nextWorkflow != null)
                {
                    nextApprovers = await (
                        from a in _context.ApprovalApprovers
                        join u in _context.Users on a.UserId equals u.UserId
                        where a.WorkflowId == nextWorkflow.WorkflowId
                        select new ApproverInfoDto
                        {
                            Name = u.FullName,
                            Role = a.Workflow.ApproverRole
                        }
                    ).ToListAsync();
                }
            }

            // Final DTO
            resultList.Add(new ApprovalRequestStatusDto
            {
                RequestId = request.RequestId,
                RequestType = request.RequestType,
                OverallStatus = request.Status,
                CurrentLevelNo = request.CurrentLevelNo,
                NextApprovers = nextApprovers,
                Levels = levelDtos,
                RejectedBy = rejectedBy
            });
        }

        return Ok(resultList);
    }


    [HttpGet("Notify_BackUpUsers")]
    public async Task<IActionResult> Notify_BackUpUsers(string status)
    {
        // base query
        var approvalQuery = _context.ApprovalRequests
            .Where(ar => ar.Status.ToUpper() == status.ToUpper() && !ar.IsExported);

        var query = from t in _context.Timesheets.Include(p => p.Employee)
                    join pr in _context.Projects on t.ProjectId equals pr.ProjectId
                    join ar in approvalQuery on t.TimesheetId equals ar.TimesheetId
                    join e in _context.Employees on t.EmployeeId equals e.EmployeeId
                    select new
                    {
                        Timesheet = t,
                        RequestId = ar.RequestId,
                        ApproverName = pr.ProjectManagerName + " (" + pr.ProjectManagerId + ")",
                        ApprovalStatus = ar.Status,
                        IsExported = ar.IsExported,
                        EmployeeDisplayName = e.DisplayedName,
                        Comment = ar.Actions
                                     .Select(a => a.ActionComment)
                                     .FirstOrDefault(),
                        Actions = ar.Actions,
                        CreatedDate = ar.CreatedAt,
                        IPAddress = ar.Actions
                                     .Select(a => a.IpAddress)
                                     .FirstOrDefault(),
                        ApproverId = pr.ProjectManagerId
                    };
        int hoursDiff = Convert.ToInt32(_config["BackUpUserNotificationinHrs"]);

        var result = await query
            .OrderBy(x => x.Timesheet.TimesheetDate ?? DateOnly.MaxValue)
            .ToListAsync();
        TimeZoneInfo easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        List<TimesheetNotifyDTO> NotifyList = new List<TimesheetNotifyDTO>();
        foreach (var x in result)
        {
            var ApprovedDate = x.Actions
                .Where(a => a.ActionDate != null)
                .OrderByDescending(a => a.ActionDate)
                .FirstOrDefault();

            x.Timesheet.ApproverId = x.ApproverId;

            if (ApprovedDate != null && ApprovedDate.ActionDate != DateTime.MinValue)
            {
                if (ApprovedDate.LevelNo < 4)
                {
                    double hours = (DateTime.UtcNow - ApprovedDate.ActionDate).TotalHours;
                    var users = await _helper.GetBackupUsersByLevel(ApprovedDate.LevelNo);
                    users = await _helper.GetBackupUsersByUser(x.Timesheet.ApproverId);
                    if (hours > hoursDiff)
                    {
                        foreach (var user in users)
                        {
                            //_emailService.SendEmailAsync(user.Email, new List<Timesheet>() { x.Timesheet }, new User() { Username = user.Username, FullName = user.FullName, Projects = new List<Project>() { new Project() { ProjectId = x.Timesheet.ProjectId, ProjectName = x.Timesheet.ProjectId } } });
                            NotifyList.Add(new TimesheetNotifyDTO() { EmployeeId = x.Timesheet.EmployeeId, DisplayedName = x.EmployeeDisplayName, User = user, TimesheetDate = x.Timesheet.TimesheetDate });
                        }
                    }
                }
            }
            else
            {
                double hours = (DateTime.UtcNow - x.CreatedDate).TotalHours;
                var users = await _helper.GetBackupUsersByLevel(1);
                users = await _helper.GetBackupUsersByUser(x.Timesheet.ApproverId);
                if (hours > hoursDiff)
                {
                    foreach (var user in users)
                    {
                        //_emailService.SendEmailAsync(user.Email, new List<Timesheet>() { x.Timesheet }, new User() { Username = user.Username, FullName = user.FullName, Projects = new List<Project>() { new Project() { ProjectId = x.Timesheet.ProjectId, ProjectName = x.Timesheet.ProjectId } } });
                        NotifyList.Add(new TimesheetNotifyDTO() { BatchId = x.Timesheet.BatchId, EmployeeId = x.Timesheet.EmployeeId, DisplayedName = x.EmployeeDisplayName, User = user, ProjectId = x.Timesheet.ProjectId, TimesheetDate = x.Timesheet.TimesheetDate });
                    }
                    //NotifyList.Add(x.Timesheet);

                }
            }
        }
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

        if (!string.IsNullOrEmpty(emailNotification) && emailNotification.ToLower() == "true")
        {
            var emails = NotifyList.Select(t => new { UserId = t.User.UserId, Email = t.User.Email, Username = t.User.Username, t.User.FullName }).Distinct().ToList();


            if (allowRedirect.ToLower() == "true" && !string.IsNullOrEmpty(redirectEmail))
            {
                foreach (var user in emails)
                {
                    await _taskQueue.QueueBackgroundWorkItemAsync(async (sp, ct) =>
                            {
                                //await _emailService.SendEmailWithRedirectAsync(email, pendingTimesheets, dto, redirectEmail);
                                var emailer = sp.GetRequiredService<EmailService>();
                                await emailer.SendEmailWithRedirectDTOAsync(user.Email, NotifyList.Where(p => p.User.Username == user.Username).ToList(), new User() { Username = user.Username, FullName = user.FullName, Projects = new List<Project>() { new Project() { ProjectId = NotifyList.Where(p => p.User.Username == user.Username).ToList().FirstOrDefault().ProjectId, ProjectName = NotifyList.Where(p => p.User.Username == user.Username).ToList().FirstOrDefault().ProjectId } } }, redirectEmail);
                            });
                }
            }
            else
            {
                foreach (var user in emails)
                {
                    await _taskQueue.QueueBackgroundWorkItemAsync(async (sp, ct) =>
                        {

                            //await _emailService.SendEmailAsync(email, pendingTimesheets, dto);
                            var emailer = sp.GetRequiredService<EmailService>();
                            await emailer.SendEmailDTOAsync(user.Email, NotifyList.Where(p => p.User.Username == user.Username).ToList(), new User() { Username = user.Username, FullName = user.FullName, Projects = new List<Project>() { new Project() { ProjectId = NotifyList.Where(p => p.User.Username == user.Username).ToList().FirstOrDefault().ProjectId, ProjectName = NotifyList.Where(p => p.User.Username == user.Username).ToList().FirstOrDefault().ProjectId } } });
                        });
                }
            }
        }


        //foreach (var user in emails)
        //{
        //    _emailService.SendEmailDTOAsync(user.Email, NotifyList.Where(p => p.User.Username == user.Username).ToList(), new User() { Username = user.Username, FullName = user.FullName, Projects = new List<Project>() { new Project() { ProjectId = NotifyList.Where(p => p.User.Username == user.Username).ToList().FirstOrDefault().ProjectId, ProjectName = NotifyList.Where(p => p.User.Username == user.Username).ToList().FirstOrDefault().ProjectId } } });
        //}


        return Ok(result.Select(P => P.Timesheet).ToList());
    }

    [HttpGet("daily-summary")]
    public async Task<IActionResult> GetDailySummary(DateOnly? date = null)
    {

        var dialyAnalysis = new DailySummaryResponse();
        // Use today's date if not passed
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // Define start & end of day (UTC assumed)
        DateTime start = targetDate.ToDateTime(TimeOnly.MinValue);
        DateTime end = targetDate.ToDateTime(TimeOnly.MaxValue);

        // Get all APPROVED actions for the day
        var approvedActions = _context.ApprovalActions.Include(a => a.Request).ThenInclude(r => r.Timesheet).ThenInclude(t => t.Employee)
            .Where(a => a.ActionDate >= start &&
                        a.ActionDate <= end)
            .ToList();


        var adminUserIds = approvedActions
            .Select(a => a.Request.Timesheet.CreatedBy)
            .Distinct()
            .ToList();

        var adminUsers = await _context.Users
            .Where(u => adminUserIds.Contains(u.Username))
            .ToListAsync();

        foreach (var admin in adminUsers)
        {
            // Total Approved

            // Group by Approver

            var approvedByApprover =
                    (from a in approvedActions.Where(a => a.Request.Timesheet.CreatedBy == admin.Username)
                     join u in _context.Users on a.ApproverId equals u.UserId
                     where a.ActionStatus.ToUpper() == "APPROVED"
                     group new { a, u } by new { u.FullName, a.LevelNo, u.Username, a.Request.Timesheet.BatchId } into g
                     select new ApprovalSummary
                     {
                         BatchId = g.Key.BatchId,
                         ApproverName = g.Key.FullName + " (" + g.Key.Username + ")",
                         Level = g.Key.LevelNo,
                         Count = g.Count()
                     })
                    .OrderByDescending(x => x.Count)
                    .ToList();


            var ApprovalSummaryByBatch = approvedByApprover
                        .GroupBy(a => a.BatchId)
                        .Select(g => new ApprovalSummaryDtos
                        {
                            BatchId = g.Key,
                            approvalSummaries = g.ToList()
                        })
                        .ToList();


            // Group by Approver

            var RejectedByApprover =
            (from a in approvedActions.Where(a => a.Request.Timesheet.CreatedBy == admin.Username)
             join u in _context.Users on a.ApproverId equals u.UserId
             where a.ActionStatus.ToUpper() == "REJECTED"
             group new { a, u } by new { u.FullName, a.LevelNo, u.Username, a.Request.Timesheet.BatchId } into g
             select new ApprovalSummary
             {
                 BatchId = g.Key.BatchId,
                 ApproverName = g.Key.FullName + " (" + g.Key.Username + ")",
                 Level = g.Key.LevelNo,
                 Count = g.Count()
             })
            .OrderByDescending(x => x.Count)
            .ToList();


            var RejectedSummaryByBatch = RejectedByApprover
            .GroupBy(a => a.BatchId)
            .Select(g => new ApprovalSummaryDtos
            {
                BatchId = g.Key,
                approvalSummaries = g.ToList()
            })
            .ToList();

            // Group by Approver

            var PendingByApprover = _context.ApprovalRequests
                    .Include(a => a.Timesheet)
                    .Include(a => a.Requester) // if you have navigation: a.Requester.FullName
                    .Where(a =>
                        a.Status.ToLower() == "pending" &&
                        a.CreatedAt >= start &&
                        a.CreatedAt <= end &&
                        a.Timesheet.CreatedBy == admin.Username
                    )
                    .GroupBy(a => new
                    {
                        a.CurrentLevelNo,
                        a.Timesheet.BatchId
                    })
                    .Select(g => new PendingSummary
                    {
                        BatchId = g.Key.BatchId,
                        Level = g.Key.CurrentLevelNo,
                        Count = g.Count()
                    })
                    .OrderByDescending(x => x.Count)
                    .ToList();

            var levelNos = PendingByApprover.Select(x => x.Level).Distinct().ToList();

            var usersForPendingLevels =
                (from aa in _context.ApprovalApprovers
                 join u in _context.Users on aa.UserId equals u.UserId
                 where levelNos.Contains(aa.Workflow.LevelNo) && u.IsActive == true
                 select new
                 {
                     LevelNo = aa.Workflow.LevelNo,
                     UserId = u.UserId,
                     u.Username,
                     u.FullName
                 })
                .ToList();

            var result = PendingByApprover
                    .Select(pb => new PendingSummary
                    {
                        BatchId = pb.BatchId,
                        Level = pb.Level,
                        Count = pb.Count,

                        Approvers = usersForPendingLevels
                            .Where(u =>
                                pb.Level == 2
                                    ? u.UserId == pb.UserId   // ✔ For Level 2 — only requester
                                    : u.LevelNo == pb.Level     // ✔ Normal case
                            )
                            .Select(a => new UserSummary
                            {
                                UserId = a.UserId,
                                Username = a.Username,
                                FullName = a.FullName
                            }).Distinct().ToList()
                    })
                    .OrderBy(x => x.Level)
                    .ToList();

            // Pending count for the same date
            var totalPending = await _context.ApprovalRequests
                .Where(ar => ar.Status == "PENDING" && ar.Timesheet.CreatedBy == admin.Username)
                .CountAsync();

            var PendingByBatch = await _context.ApprovalRequests
               .Where(ar => ar.Status == "PENDING" && ar.Timesheet.CreatedBy == admin.Username)
                .GroupBy(a => a.Timesheet.BatchId)
                        .Select(g => new BatchStatus
                        {
                            BatchId = g.Key,
                            Count = g.Count()
                        })
                        .ToListAsync();

            // Rejected count for the same date

            var rejectedByBatch = await _context.ApprovalActions
                        .Where(a =>
                            a.ActionStatus == "REJECTED" &&
                            a.Request.Timesheet.CreatedBy == admin.Username &&
                            a.ActionDate >= start &&
                            a.ActionDate <= end
                        )
                        .GroupBy(a => a.Request.Timesheet.BatchId)
                        .Select(g => new BatchStatus
                        {
                            BatchId = g.Key,
                            Count = g.Count()
                        })
                        .ToListAsync();

            var totalRejected = await _context.ApprovalActions
                .Where(a => a.ActionStatus == "REJECTED" && a.Request.Timesheet.CreatedBy == admin.Username &&
                            a.ActionDate >= start &&
                            a.ActionDate <= end)
                .CountAsync();

            var totalApproved = await _context.ApprovalRequests
                .Where(a => a.Status == "APPROVED" && a.Timesheet.CreatedBy == admin.Username &&
                            a.Actions.OrderByDescending(p => p.ActionDate).FirstOrDefault().ActionDate >= start &&
                            a.Actions.OrderByDescending(p => p.ActionDate).FirstOrDefault().ActionDate <= end)
                .CountAsync();

            var ApprovedByBatch = await _context.ApprovalActions
                    .Where(a =>
                        a.ActionStatus == "APPROVED" &&
                        a.Request.Timesheet.CreatedBy == admin.Username &&
                        a.ActionDate >= start &&
                        a.ActionDate <= end
                    )
                    .GroupBy(a => a.Request.Timesheet.BatchId)
                    .Select(g => new BatchStatus
                    {
                        BatchId = g.Key,
                        Count = g.Count()
                    })
                    .ToListAsync();
            dialyAnalysis = new DailySummaryResponse
            {
                Date = targetDate.ToString(),
                TotalApproved = totalApproved,
                ApprovedByApprover = ApprovalSummaryByBatch,
                RejectedByApprover = RejectedSummaryByBatch,
                PendingByApprover = result,
                TotalPending = totalPending,
                TotalRejected = totalRejected,
                ApprovedBatchStatus = ApprovedByBatch,
                RejectedBatchStatus = rejectedByBatch,
                PendingBatchStatus = PendingByBatch
            };

            await _emailService.SendDailySummaryEmailAsync(admin.Email, dialyAnalysis);
            //await _emailService.SendDailySummaryEmailAsync("paul.papnol@revolvespl.com", dialyAnalysis);

        }
        return Ok(dialyAnalysis);
    }

    [HttpPost("ImportTimesheet")]
    public async Task<IActionResult> ImportTimesheetExcel(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is empty");

        var result = new List<Timesheet>();

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);

        stream.Position = 0;

        IWorkbook workbook = WorkbookFactory.Create(stream);
        var sheet = workbook.GetSheetAt(0);

        int rowIndex = 1; // row 0 is header

        ExcelHelper excelHelper = new ExcelHelper();

        while (sheet.GetRow(rowIndex) != null)
        {
            var row = sheet.GetRow(rowIndex);

            var dto = new Timesheet
            {
                TimesheetDate = excelHelper.GetDate(row.GetCell(0)),
                EmployeeId = excelHelper.GetString(row.GetCell(1)),
                TimesheetTypeCode = excelHelper.GetString(row.GetCell(2)),
                WorkingState = excelHelper.GetString(row.GetCell(3)),
                FiscalYear = excelHelper.GetInt(row.GetCell(4)),
                Period = excelHelper.GetInt(row.GetCell(5)),
                Subperiod = excelHelper.GetInt(row.GetCell(6)),
                CorrectingRefDate = excelHelper.GetDate(row.GetCell(7)),
                PayType = excelHelper.GetString(row.GetCell(8)),
                GeneralLaborCategory = excelHelper.GetString(row.GetCell(9)),
                TimesheetLineTypeCode = excelHelper.GetString(row.GetCell(10)),
                LaborCostAmount = excelHelper.GetDecimal(row.GetCell(11)),
                Hours = excelHelper.GetDecimal(row.GetCell(12)),
                WorkersCompCode = excelHelper.GetString(row.GetCell(13)),
                LaborLocationCode = excelHelper.GetString(row.GetCell(14)),
                OrganizationId = excelHelper.GetString(row.GetCell(15)),
                AccountId = excelHelper.GetString(row.GetCell(16)),
                ProjectId = excelHelper.GetString(row.GetCell(17)),
                ProjectLaborCategory = excelHelper.GetString(row.GetCell(18)),
                ReferenceNumber1 = excelHelper.GetString(row.GetCell(19)),
                ReferenceNumber2 = excelHelper.GetString(row.GetCell(20)),
                OrganizationAbbreviation = excelHelper.GetString(row.GetCell(21)),
                ProjectAbbreviation = excelHelper.GetString(row.GetCell(22)),
                SequenceNumber = excelHelper.GetInt(row.GetCell(23)),
                EffectiveBillingDate = excelHelper.GetDate(row.GetCell(24)),
                ProjectAccountAbbrev = excelHelper.GetString(row.GetCell(25)),
                MultiStateCode = excelHelper.GetString(row.GetCell(26)),
                ReferenceSequenceNum = excelHelper.GetInt(row.GetCell(27)),
                TimesheetLineDate = excelHelper.GetDate(row.GetCell(28)),
                Notes = excelHelper.GetString(row.GetCell(29))
            };
            if (!string.IsNullOrEmpty(dto.TimesheetTypeCode) && (dto.TimesheetTypeCode.ToUpper() == "R" || dto.TimesheetTypeCode.ToUpper() == "C"))
            {
                result.Add(dto);
            }
            rowIndex++;
        }

        //TODO: Save to DB after mapping to entity
        _context.Timesheets.AddRange(result);
        await _context.SaveChangesAsync();

        return Ok(new { Count = result.Count, Data = result });
    }

    [HttpPost("import-excel-s3")]
    public async Task<IActionResult> ImportTimesheetsexcelS3Async(string filename, string Username, int Month, int Year)
    {
        var timesheets = new List<Timesheet>();
        var skippedRecords = new List<string>();
        var badRecords = new List<string>();
        var ImportedRecords = new List<string>();
        int imported = 0, skipped = 0;
        ExcelHelper excelHelper = new ExcelHelper();

        // Cache existing timesheets to reduce DB hits t.EmployeeId, t.PayType, t.OrganizationId, t.AccountId, t.ProjectId, t.ProjectLaborCategory, t.LaborLocationCode }
        var existingRecords = _context.Timesheets
            .Select(t => new { t.EmployeeId, t.TimesheetDate, t.PayType, t.ProjectId, t.OrganizationId, t.AccountId, t.ProjectLaborCategory, t.LaborLocationCode, t.TimesheetTypeCode, t.SequenceNumber, t.Period, t.FiscalYear })
            .ToHashSet();
        int rowIndex = 0;

        //using var stream = file.OpenReadStream();
        //using var reader = new StreamReader(stream);
        // Create S3 client with credentials and region
        var credentials = new BasicAWSCredentials(AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY);
        var s3Client = new AmazonS3Client(credentials, Amazon.RegionEndpoint.GetBySystemName(Region));
        string csvText;
        using (var response = await s3Client.GetObjectAsync(BucketName, filename))
        using (var memoryStream = new MemoryStream())
        {
            await response.ResponseStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            //var stream = file.OpenReadStream();
            IWorkbook workbook = new XSSFWorkbook(memoryStream);  // XLSX
            ISheet sheet = workbook.GetSheetAt(0);
            for (int i = 1; i <= sheet.LastRowNum; i++)
            {
                var row = sheet.GetRow(i);
                if (row == null) continue;

                var timesheetDate = excelHelper.GetDate(row.GetCell(1));
                var timesheetType = excelHelper.GetString(row.GetCell(3));

                int? SequenceNo = excelHelper.GetInt(row.GetCell(24));
                var employeeId = excelHelper.GetString(row.GetCell(2));
                var timesheetTypeCode = excelHelper.GetString(row.GetCell(3));
                var ProjectId = excelHelper.GetString(row.GetCell(18));
                //int Month = excelHelper.GetInt(row.GetCell(6)); ;
                //int Year = excelHelper.GetInt(row.GetCell(5));
                var dateTime = timesheetDate.GetValueOrDefault().ToDateTime(TimeOnly.MinValue);

                var PayType = excelHelper.GetString(row.GetCell(9));
                var OrganizationId = excelHelper.GetString(row.GetCell(16));
                var AccountId = excelHelper.GetString(row.GetCell(17));
                var ProjectLaborCategory = excelHelper.GetString(row.GetCell(19));
                var LaborLocationCode = excelHelper.GetString(row.GetCell(15));


                int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)dateTime.DayOfWeek + 7) % 7;
                timesheetDate = DateOnly.FromDateTime(dateTime.AddDays(daysUntilSunday));

                if (string.IsNullOrEmpty(employeeId) || timesheetDate == null)
                {
                    //skipped++;
                    badRecords.Add(excelHelper.RowToCsv(row));
                    continue;
                }

                // ✅ Fast in-memory duplicate check

                //.Select(t => new { t.EmployeeId, t.TimesheetDate, t.PayType, t.ProjectId, t.OrganizationId, t.AccountId, t.ProjectLaborCategory, t.LaborLocationCode })
                if (ProjectId == "C1800.C.063.F.000.11")
                {

                }

                var key = new { EmployeeId = employeeId, TimesheetDate = timesheetDate, PayType = PayType, ProjectId = ProjectId, OrganizationId = OrganizationId, AccountId = AccountId, ProjectLaborCategory = ProjectLaborCategory, LaborLocationCode = LaborLocationCode, TimesheetTypeCode = timesheetType, SequenceNumber = SequenceNo, Period = Month, FiscalYear = Year };
                //var key = new { EmployeeId = employeeId, TimesheetDate = timesheetDate, TimesheetTypeCode = timesheetTypeCode, ProjectId = ProjectId, Period = Month, FiscalYear = Year, SequenceNumber = SequenceNo };
                if (existingRecords.Contains(key))
                {
                    skipped++;
                    skippedRecords.Add(excelHelper.RowToCsv(row));
                    continue;
                }

                // Add to cache to prevent duplicates within same import batch
                //existingRecords.Add(key);

                var dto = new Timesheet
                {
                    TimesheetDate = timesheetDate,
                    EmployeeId = excelHelper.GetString(row.GetCell(2)),
                    TimesheetTypeCode = excelHelper.GetString(row.GetCell(3)),
                    WorkingState = excelHelper.GetString(row.GetCell(4)),
                    FiscalYear = Year,
                    Period = Month,
                    Subperiod = excelHelper.GetInt(row.GetCell(7)),
                    CorrectingRefDate = excelHelper.GetDate(row.GetCell(8)),
                    PayType = excelHelper.GetString(row.GetCell(9)),
                    GeneralLaborCategory = excelHelper.GetString(row.GetCell(10)),
                    TimesheetLineTypeCode = excelHelper.GetString(row.GetCell(11)),
                    LaborCostAmount = excelHelper.GetDecimal(row.GetCell(12)),
                    Hours = excelHelper.GetDecimal(row.GetCell(13)),
                    WorkersCompCode = excelHelper.GetString(row.GetCell(14)),
                    LaborLocationCode = excelHelper.GetString(row.GetCell(15)),
                    OrganizationId = excelHelper.GetString(row.GetCell(16)),
                    AccountId = excelHelper.GetString(row.GetCell(17)),
                    ProjectId = excelHelper.GetString(row.GetCell(18)),
                    ProjectLaborCategory = excelHelper.GetString(row.GetCell(19)),
                    ReferenceNumber1 = excelHelper.GetString(row.GetCell(20)),
                    ReferenceNumber2 = excelHelper.GetString(row.GetCell(21)),
                    OrganizationAbbreviation = excelHelper.GetString(row.GetCell(22)),
                    ProjectAbbreviation = excelHelper.GetString(row.GetCell(23)),
                    SequenceNumber = excelHelper.GetInt(row.GetCell(24)),
                    EffectiveBillingDate = excelHelper.GetDate(row.GetCell(25)),
                    ProjectAccountAbbrev = excelHelper.GetString(row.GetCell(26)),
                    MultiStateCode = excelHelper.GetString(row.GetCell(27)),
                    ReferenceSequenceNum = excelHelper.GetInt(row.GetCell(28)),
                    TimesheetLineDate = excelHelper.GetDate(row.GetCell(29)),
                    Notes = excelHelper.GetString(row.GetCell(30)),
                    CreatedBy = Username,
                    ModifiedBy = Username,
                    BatchId = new string((employeeId ?? "").Take(3).ToArray())
                                      + (excelHelper.GetString(row.GetCell(5)) ?? "")
                                      + (excelHelper.GetString(row.GetCell(6)) ?? "")
                                      + (excelHelper.GetString(row.GetCell(24)) ?? "")
                };
                if (!string.IsNullOrEmpty(dto.TimesheetTypeCode) && (dto.TimesheetTypeCode.ToUpper() == "R" || dto.TimesheetTypeCode.ToUpper() == "C"))
                {
                    timesheets.Add(dto);
                    ImportedRecords.Add(excelHelper.RowToCsv(row));
                    imported++;
                }
            }
        }
        // ✅ Bulk insert for performance (if supported)
        if (timesheets.Any())
        {

            var groupedTimesheets = timesheets
                .GroupBy(t => new { t.EmployeeId, t.PayType, t.OrganizationId, t.AccountId, t.ProjectId, t.ProjectLaborCategory, t.LaborLocationCode })
                .Select(g =>
                {
                    var first = g.First();

                    return new Timesheet
                    {
                        EmployeeId = g.Key.EmployeeId,
                        PayType = g.Key.PayType,
                        OrganizationId = g.Key.OrganizationId,
                        AccountId = g.Key.AccountId,
                        ProjectId = g.Key.ProjectId,
                        ProjectLaborCategory = g.Key.ProjectLaborCategory,
                        LaborLocationCode = g.Key.LaborLocationCode,

                        TimesheetDate = first.TimesheetDate,
                        TimesheetTypeCode = first.TimesheetTypeCode,
                        WorkingState = first.WorkingState,
                        FiscalYear = first.FiscalYear,
                        Period = first.Period,
                        Subperiod = first.Subperiod,
                        CorrectingRefDate = first.CorrectingRefDate,
                        GeneralLaborCategory = first.GeneralLaborCategory,
                        TimesheetLineTypeCode = first.TimesheetLineTypeCode,
                        LaborCostAmount = first.LaborCostAmount,
                        Hours = g.Sum(x => x.Hours), // ✅ summed
                        WorkersCompCode = first.WorkersCompCode,
                        ReferenceNumber1 = first.ReferenceNumber1,
                        ReferenceNumber2 = first.ReferenceNumber2,
                        OrganizationAbbreviation = first.OrganizationAbbreviation,
                        ProjectAbbreviation = first.ProjectAbbreviation,
                        SequenceNumber = first.SequenceNumber,
                        EffectiveBillingDate = first.EffectiveBillingDate,
                        ProjectAccountAbbrev = first.ProjectAccountAbbrev,
                        MultiStateCode = first.MultiStateCode,
                        ReferenceSequenceNum = first.ReferenceSequenceNum,
                        TimesheetLineDate = first.TimesheetLineDate,
                        Notes = first.Notes,
                        CreatedBy = first.CreatedBy,
                        ModifiedBy = first.ModifiedBy,
                        BatchId = first.BatchId
                    };
                })
                    .ToList();



            var TsDate = groupedTimesheets
                .Where(t => t.TimesheetDate.HasValue &&
                            t.TimesheetDate.Value.DayOfWeek == DayOfWeek.Sunday) // Only Sundays
                .Select(t => t.TimesheetDate.Value)                        // Strip time
                .Distinct()                                                     // Remove duplicates
                .OrderBy(d => d)                                                // Optional: sort
                .ToList();
            if (TsDate.Count() == 1)
            {
                foreach (var t in groupedTimesheets)
                {
                    t.TimesheetDate = TsDate[0];
                    t.CreatedDate = DateTime.SpecifyKind(t.CreatedDate, DateTimeKind.Unspecified);
                    t.ModifiedDate = DateTime.SpecifyKind(t.ModifiedDate, DateTimeKind.Unspecified);
                }
            }
            try
            {
                await _context.BulkInsertAsync(groupedTimesheets);
                await _context.SaveChangesAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "23503")
            {
                // 23503 = foreign key violation
                var match = System.Text.RegularExpressions.Regex.Match(ex.Detail ?? "", @"Key \(employee_id\)=\((.*?)\)");
                var employeeId = match.Success ? match.Groups[1].Value : "unknown";

                return BadRequest(new
                {
                    Message = $"Import failed: Employee ID '{employeeId}' does not exist in the Employee table. " +
                              "Please check your data and try again."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "An unexpected error occurred during import." });
            }
        }

        // ✅ Return skipped records as downloadable CSV
        if (skippedRecords.Any())
        {
            var csvContent = "Skipped Records (" + skippedRecords.Count() + ")";
            csvContent += Environment.NewLine;
            csvContent += string.Join(Environment.NewLine, skippedRecords);
            csvContent += Environment.NewLine;
            csvContent += string.Join(Environment.NewLine, new List<string>() { "Imported Records(" + ImportedRecords.Count() + ")" });
            csvContent += Environment.NewLine;
            csvContent += string.Join(Environment.NewLine, ImportedRecords);
            var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
            var outputFile = new FileContentResult(bytes, "text/csv")
            {
                FileDownloadName = "SkippedRecords.csv"
            };

            return File(bytes, "text/csv", "SkippedRecords.csv");
        }

        return Ok(new { imported, skipped, message = $"{imported} imported, {skipped} skipped." });
    }


    //[HttpPost("TestImportV2")]
    //[RequestSizeLimit(200_000_000)]
    //[RequestFormLimits(MultipartBodyLengthLimit = 200_000_000)]
    //public async Task<IActionResult> TestImportV2(IFormFile file, string Username)
    //{
    //    if (file == null || file.Length == 0)
    //        return BadRequest("File missing");

    //    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    //    //string connString =
    //    //    "Host=dpg-d0n1vd2li9vc7380m3o0-a.singapore-postgres.render.com;Database=planning_demo;Username=myuser;Password=ODIfyKykuj6zdwchsnqAzccSMNeRgGQ7;Include Error Detail=true;";

    //    // 1️⃣ Save uploaded file to temp disk (/tmp in Docker)
    //    var tempFile = Path.Combine(
    //        Path.GetTempPath(),
    //        $"{Guid.NewGuid()}_{file.FileName}");

    //    try
    //    {
    //        // Copy upload → temp file (STREAMING)
    //        using (var fs = new FileStream(
    //            tempFile,
    //            FileMode.Create,
    //            FileAccess.Write,
    //            FileShare.None,
    //            bufferSize: 64 * 1024,
    //            useAsync: true))
    //        {
    //            await file.CopyToAsync(fs);
    //        }


    //        // 2️⃣ Open seekable stream
    //        using var excelStream = new FileStream(
    //            tempFile,
    //            FileMode.Open,
    //            FileAccess.Read,
    //            FileShare.Read,
    //            bufferSize: 64 * 1024,
    //            useAsync: false);

    //        using var reader = ExcelReaderFactory.CreateReader(excelStream);

    //        do
    //        {
    //            try
    //            {
    //                reader.Read(); // header row

    //                stream.Position = 0;

    //                IWorkbook workbook = WorkbookFactory.Create(stream);
    //                var sheet = workbook.GetSheetAt(0);

    //                int rowIndex = 1; // row 0 is header

    //                ExcelHelper excelHelper = new ExcelHelper();

    //                while (sheet.GetRow(rowIndex) != null)
    //                {
    //                    var row = sheet.GetRow(rowIndex);

    //                    var dto = new Timesheet
    //                    {
    //                        TimesheetDate = excelHelper.GetDate(row.GetCell(0)),
    //                        EmployeeId = excelHelper.GetString(row.GetCell(1)),
    //                        TimesheetTypeCode = excelHelper.GetString(row.GetCell(2)),
    //                        WorkingState = excelHelper.GetString(row.GetCell(3)),
    //                        FiscalYear = excelHelper.GetInt(row.GetCell(4)),
    //                        Period = excelHelper.GetInt(row.GetCell(5)),
    //                        Subperiod = excelHelper.GetInt(row.GetCell(6)),
    //                        CorrectingRefDate = excelHelper.GetDate(row.GetCell(7)),
    //                        PayType = excelHelper.GetString(row.GetCell(8)),
    //                        GeneralLaborCategory = excelHelper.GetString(row.GetCell(9)),
    //                        TimesheetLineTypeCode = excelHelper.GetString(row.GetCell(10)),
    //                        LaborCostAmount = excelHelper.GetDecimal(row.GetCell(11)),
    //                        Hours = excelHelper.GetDecimal(row.GetCell(12)),
    //                        WorkersCompCode = excelHelper.GetString(row.GetCell(13)),
    //                        LaborLocationCode = excelHelper.GetString(row.GetCell(14)),
    //                        OrganizationId = excelHelper.GetString(row.GetCell(15)),
    //                        AccountId = excelHelper.GetString(row.GetCell(16)),
    //                        ProjectId = excelHelper.GetString(row.GetCell(17)),
    //                        ProjectLaborCategory = excelHelper.GetString(row.GetCell(18)),
    //                        ReferenceNumber1 = excelHelper.GetString(row.GetCell(19)),
    //                        ReferenceNumber2 = excelHelper.GetString(row.GetCell(20)),
    //                        OrganizationAbbreviation = excelHelper.GetString(row.GetCell(21)),
    //                        ProjectAbbreviation = excelHelper.GetString(row.GetCell(22)),
    //                        SequenceNumber = excelHelper.GetInt(row.GetCell(23)),
    //                        EffectiveBillingDate = excelHelper.GetDate(row.GetCell(24)),
    //                        ProjectAccountAbbrev = excelHelper.GetString(row.GetCell(25)),
    //                        MultiStateCode = excelHelper.GetString(row.GetCell(26)),
    //                        ReferenceSequenceNum = excelHelper.GetInt(row.GetCell(27)),
    //                        TimesheetLineDate = excelHelper.GetDate(row.GetCell(28)),
    //                        Notes = excelHelper.GetString(row.GetCell(29))
    //                    };
    //                    if (!string.IsNullOrEmpty(dto.TimesheetTypeCode) && (dto.TimesheetTypeCode.ToUpper() == "R" || dto.TimesheetTypeCode.ToUpper() == "C"))
    //                    {
    //                        result.Add(dto);
    //                    }
    //                    rowIndex++;
    //                }

    //                //TODO: Save to DB after mapping to entity
    //                _context.Timesheets.AddRange(result);
    //                await _context.SaveChangesAsync();


    //            }
    //            catch (Exception ex)
    //            {

    //            }

    //        } while (reader.NextResult());

    //        return Ok("Import completed successfully");
    //    }
    //    finally
    //    {
    //        // 3️⃣ Cleanup temp file
    //        if (System.IO.File.Exists(tempFile))
    //            System.IO.File.Delete(tempFile);
    //    }
    //}

    [NonAction]
    static string GetCellString(ICell cell)
    {
        if (cell == null || cell.CellType == CellType.Blank) return "";

        return cell.CellType switch
        {
            CellType.Numeric => DateUtil.IsCellDateFormatted(cell)
                 ? cell.DateCellValue?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""
                : cell.NumericCellValue.ToString("0.########", CultureInfo.InvariantCulture),
            CellType.String => EscapeCsv(cell.StringCellValue),
            CellType.Formula => cell.NumericCellValue.ToString("0.########", CultureInfo.InvariantCulture),
            CellType.Boolean => cell.BooleanCellValue ? "TRUE" : "FALSE",
            _ => EscapeCsv(cell.ToString())
        };
    }

    [NonAction]
    static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
    [NonAction]
    public string GetTableNameFromSheetName(string value)
    {
        switch (value.ToLower())
        {
            case "lab_hours":
                return "lab_hours";
            case "psr_final_data":
                return "psr_final_data";
            case "psr_header":
                return "psr_header";
            case "project_modifications":
                return "project_modifications";
            case "plc_codes":
                return "plc_codes";
            case "gl_post_details":
                return "gl_post_details";
            //case "lab_hours":
            //    return "lab_hours";
            //case "psr_final_data":
            //    return "psr_final_data";
            default:
                throw new Exception($"No target table mapping for sheet '{value}'");
        }
    }

}
