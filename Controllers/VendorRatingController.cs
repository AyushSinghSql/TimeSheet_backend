using ExcelDataReader;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Data;
using System.Globalization;
using System.Text;
using TimeSheet.Models;

namespace TimeSheet.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VendorRatingController : ControllerBase
    {
        private readonly AppDbContext _context;

        public VendorRatingController(AppDbContext context)
        {
            _context = context;
        }

        // CREATE
        [HttpPost("CreateQuestionCategory")]
        public async Task<IActionResult> CreateQuestionCategory(QuestionCategoryMaster model)
        {
            model.CreatedDate = DateTime.UtcNow;
            _context.QuestionCategories.Add(model);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetQuestionCategoryById), new { id = model.CategoryId }, model);
        }

        // GET ALL
        [HttpGet("GetAllQuestionCategories")]
        public async Task<IActionResult> GetAllQuestionCategories()
        {
            var data = await _context.QuestionCategories.ToListAsync();
            return Ok(data);
        }

        // GET BY ID
        [HttpGet("GetQuestionCategoryById/{id}")]
        public async Task<IActionResult> GetQuestionCategoryById(long id)
        {
            var data = await _context.QuestionCategories.FindAsync(id);

            if (data == null)
                return NotFound("Category not found");

            return Ok(data);
        }

        // UPDATE
        [HttpPut("UpdateQuestionCategory/{id}")]
        public async Task<IActionResult> UpdateQuestionCategory(long id, QuestionCategoryMaster model)
        {
            var existing = await _context.QuestionCategories.FindAsync(id);

            if (existing == null)
                return NotFound("Category not found");

            existing.CategoryName = model.CategoryName;
            existing.Description = model.Description;
            existing.VendorTypeId = model.VendorTypeId;
            existing.CategoryWeightage = model.CategoryWeightage;
            existing.ActiveFlag = model.ActiveFlag;

            await _context.SaveChangesAsync();

            return Ok(existing);
        }

        // DELETE
        [HttpDelete("ForceDeleteQuestionCategory/{id}")]
        public async Task<IActionResult> ForceDeleteQuestionCategory(long id)
        {
            var existing = await _context.QuestionCategories.FindAsync(id);

            if (existing == null)
                return NotFound("Category not found");

            _context.QuestionCategories.Remove(existing);
            await _context.SaveChangesAsync();

            return Ok("Deleted successfully");
        }

        [HttpDelete("DeleteQuestionCategory/{id}")]
        public async Task<IActionResult> DeleteQuestionCategory(long id)
        {
            var categoryExists = await _context.QuestionCategories
                .AnyAsync(c => c.CategoryId == id);

            if (!categoryExists)
                return NotFound("Category not found");

            var hasQuestions = await _context.Questions
                .AnyAsync(q => q.CategoryId == id);

            if (hasQuestions)
                return BadRequest("Cannot delete category because questions exist under it.");

            _context.QuestionCategories.Remove(new QuestionCategoryMaster { CategoryId = id });
            await _context.SaveChangesAsync();

            return Ok("Deleted successfully");
        }

        // CREATE
        [HttpPost("QuestionMaster")]
        public async Task<IActionResult> CreateQuestionMaster(QuestionMaster model)
        {
            var categoryExists = await _context.QuestionCategories
                .AnyAsync(c => c.CategoryId == model.CategoryId);

            if (!categoryExists)
                return BadRequest("Invalid CategoryId");

            model.CreatedDate = DateTime.UtcNow;
            _context.Questions.Add(model);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetQuestionById), new { id = model.QuestionId }, model);
        }

        // GET ALL
        [HttpGet("GetAllQuestions")]
        public async Task<IActionResult> GetAllQuestions()
        {
            var data = await _context.Questions
                .Include(q => q.Category)
                .ToListAsync();

            return Ok(data);
        }

        // GET BY ID
        [HttpGet("GetQuestionById/{id}")]
        public async Task<IActionResult> GetQuestionById(long id)
        {
            var data = await _context.Questions
                .Include(q => q.Category)
                .FirstOrDefaultAsync(q => q.QuestionId == id);

            if (data == null)
                return NotFound("Question not found");

            return Ok(data);
        }

        // UPDATE
        [HttpPut("UpdateQuestion/{id}")]
        public async Task<IActionResult> UpdateQuestion(long id, QuestionMaster model)
        {
            var existing = await _context.Questions.FindAsync(id);

            if (existing == null)
                return NotFound("Question not found");

            existing.CategoryId = model.CategoryId;
            existing.QuestionText = model.QuestionText;
            existing.QuestionType = model.QuestionType;
            //existing.Weightage = model.Weightage;
            existing.MandatoryFlag = model.MandatoryFlag;
            //existing.VendorTypeId = model.VendorTypeId;
            existing.ActiveFlag = model.ActiveFlag;
            //existing.VersionNo += 1; // increment version
            existing.SequenceNo = model.SequenceNo; // increment version


            await _context.SaveChangesAsync();

            return Ok(existing);
        }

        //// DELETE
        //[HttpDelete("ForceDeleteQuestion/{id}")]
        //public async Task<IActionResult> ForceDeleteQuestion(long id)
        //{

        //    var existing = await _context.Questions.FindAsync(id);

        //    if (existing == null)
        //        return NotFound("Question not found");

        //    _context.Questions.Remove(existing);
        //    await _context.SaveChangesAsync();

        //    return Ok("Deleted successfully");
        //}

        // DELETE
        [HttpDelete("ForceDeleteQuestion/{id}")]
        public async Task<IActionResult> ForceDeleteQuestion(long id)
        {
            var existing = await _context.Questions.FindAsync(id);

            if (existing == null)
                return NotFound("Question not found");

            // 1️⃣ Delete all answers related to this question
            var relatedAnswers = await _context.VendorQuestionAnswers
                .Where(a => a.QuestionId == id)
                .ToListAsync();

            if (relatedAnswers.Any())
                _context.VendorQuestionAnswers.RemoveRange(relatedAnswers);

            // 2️⃣ Delete question
            _context.Questions.Remove(existing);

            await _context.SaveChangesAsync();

            return Ok("Question and related answers deleted successfully");
        }

        [HttpDelete("DeleteQuestion/{id}")]
        public async Task<IActionResult> DeleteQuestion(long id)
        {
            var existing = await _context.Questions
                .FirstOrDefaultAsync(q => q.QuestionId == id);

            if (existing == null)
                return NotFound("Question not found");

            // 🔎 Check if any answers exist for this question
            var hasAnswers = await _context.VendorQuestionAnswers
                .AnyAsync(a => a.QuestionId == id);

            if (hasAnswers)
                return BadRequest("Cannot delete question because answers exist.");

            _context.Questions.Remove(existing);
            await _context.SaveChangesAsync();

            return Ok("Deleted successfully");
        }
        // POST: api/QuestionOption
        [HttpPost("AddOption")]
        public async Task<IActionResult> AddOption([FromBody] QuestionOptionMaster option)
        {
            // 1️⃣ Validate Question exists
            var questionExists = await _context.Questions
                .AnyAsync(q => q.QuestionId == option.QuestionId);

            if (!questionExists)
                return BadRequest($"QuestionId {option.QuestionId} does not exist.");

            // 2️⃣ Optional: Prevent negative score
            if (option.ScoreValue < 0)
                return BadRequest("ScoreValue must be >= 0.");

            // 3️⃣ Ignore navigation property if provided
            option.Question = null;

            // 4️⃣ Add option
            _context.Options.Add(option);
            await _context.SaveChangesAsync();

            // 5️⃣ Return Created response with new ID
            return Ok("Option Added");
        }

        [HttpGet("categories")]
        public async Task<IActionResult> GetAllCategories()
        {
            var categories = await _context.QuestionCategories
                .Where(c => c.ActiveFlag)
                .Include(c => c.Questions.Where(q => q.ActiveFlag))
                    .ThenInclude(q => q.Options)
                .ToListAsync();

            var result = categories.Select(c => new CategoryWithQuestionsDto
            {
                CategoryId = c.CategoryId,
                CategoryName = c.CategoryName,
                Description = c.Description,
                CategoryWeightage = c.CategoryWeightage,
                Questions = c.Questions.Select(q => new QuestionDto
                {
                    QuestionId = q.QuestionId,
                    QuestionText = q.QuestionText,
                    ActiveFlag = q.ActiveFlag,
                    MandatoryFlag = q.MandatoryFlag,
                    SequenceNo = q.SequenceNo,
                    Options = q.Options.Select(o => new QuestionOptionDto
                    {
                        OptionId = o.OptionId,
                        OptionText = o.OptionText
                    }).ToList()
                }).ToList()
            }).ToList();

            return Ok(result);
        }

        [HttpGet("categoriesV1")]
        public async Task<IActionResult> GetAllCategoriesV1()
        {
            var categories = await _context.QuestionCategories
                .Include(c => c.Questions)
                    .ThenInclude(q => q.Options)
                .ToListAsync();

            var result = categories.Select(c => new CategoryWithQuestionsDto
            {
                CategoryId = c.CategoryId,
                CategoryName = c.CategoryName,
                Description = c.Description,
                CategoryWeightage = c.CategoryWeightage,
                Questions = c.Questions.Select(q => new QuestionDto
                {
                    QuestionId = q.QuestionId,
                    QuestionText = q.QuestionText,
                    ActiveFlag = q.ActiveFlag,
                    MandatoryFlag = q.MandatoryFlag,
                    SequenceNo = q.SequenceNo,
                    Options = q.Options.Select(o => new QuestionOptionDto
                    {
                        OptionId = o.OptionId,
                        OptionText = o.OptionText
                    }).ToList()
                }).ToList()
            }).ToList();

            return Ok(result);
        }

        // ✅ Add Vendor Evaluation
        [HttpPost("AddVendorRating")]
        public async Task<ActionResult<VendorEvaluation>> AddVendorEvaluation(VendorEvaluation model)
        {
            model.CreatedDate = DateTime.UtcNow;
            model.Status ??= "DRAFT";

            _context.VendorEvaluations.Add(model);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetVendorEvaluationById),
                new { id = model.EvaluationId }, model);
        }

        // ✅ Get All
        [HttpGet("GetAllVendorRatings")]
        public async Task<ActionResult<IEnumerable<VendorEvaluation>>> GetAllVendorRatings()
        {
            return await _context.VendorEvaluations.ToListAsync();
        }

        // ✅ Get By Id
        [HttpGet("GetVendorRating/{id}")]
        public async Task<ActionResult<VendorEvaluation>> GetVendorEvaluationById(long id)
        {
            var evaluation = await _context.VendorEvaluations.FindAsync(id);

            if (evaluation == null)
                return NotFound();

            return evaluation;
        }

        [HttpGet("GetAllVendors")]
        public async Task<IActionResult> GetAllVendors()
        {
            var vendors = (await _context.Vendors
                    .AsNoTracking()
                    .Select(v => new VendorDto
                    {
                        VendorId = v.VendorId,
                        Name = v.Name,
                        Type = v.Type
                    })
                    .ToListAsync())
                .DistinctBy(v => v.VendorId)
                .OrderBy(v => v.Name)
                .ToList();

            return Ok(vendors);
        }

        // POST: api/VendorQuestionAnswer
        [HttpPost("AddAnswer")]
        public async Task<ActionResult<VendorQuestionAnswer>> AddAnswer(VendorQuestionAnswer model)
        {
            model.CreatedDate = DateTime.UtcNow;

            _context.VendorQuestionAnswers.Add(model);
            await _context.SaveChangesAsync();

            return Ok("Answer Added");
            //return CreatedAtAction(nameof(GetAnswerById), new { id = model.AnswerId }, model);
        }

        [HttpPost("AddOrUpdateAnswer")]
        public async Task<IActionResult> AddOrUpdateAnswer(VendorQuestionAnswer model)
        {
            var existing = await _context.VendorQuestionAnswers
                .FirstOrDefaultAsync(a => a.VendorId == model.VendorId && a.QuestionId == model.QuestionId);

            if (existing == null)
            {
                model.CreatedDate = DateTime.UtcNow;
                _context.VendorQuestionAnswers.Add(model);
            }
            else
            {
                existing.SelectedOptionId = model.SelectedOptionId;
                existing.ScoreAchieved = model.ScoreAchieved;
                existing.CreatedDate = DateTime.UtcNow;
                //existing. = model.UpdatedBy;
            }

            await _context.SaveChangesAsync();
            return Ok("Answer processed successfully.");
        }

        // GET: api/VendorQuestionAnswer/vendor/1
        [HttpGet("GetVendorQuestionsWithAnswers/{vendorId}")]
        public async Task<ActionResult<List<VendorQuestionCategoryDto>>> GetVendorQuestionsWithAnswers(string vendorId)
        {
            // Load all categories
            var categories = await _context.QuestionCategories
                .Include(c => c.Questions)
                    .ThenInclude(q => q.Options)
                .ToListAsync();

            // Load all answers for vendor
            var answers = await _context.VendorQuestionAnswers
                .Where(a => a.VendorId == vendorId)
                .ToListAsync();

            // Build the final DTO
            var result = categories.Select(c => new VendorQuestionCategoryDto
            {
                CategoryId = c.CategoryId,
                CategoryName = c.CategoryName,
                Description = c.Description,
                //CategoryWeightage = c.Weightage,
                Questions = c.Questions.Select(q =>
                {
                    var answer = answers.FirstOrDefault(a => a.QuestionId == q.QuestionId);

                    return new VendorQuestionDto
                    {
                        QuestionId = q.QuestionId,
                        SequenceNo = q.SequenceNo,
                        QuestionText = q.QuestionText,
                        Options = q.Options.Select(o => new QuestionOptionDtoV1
                        {
                            OptionId = o.OptionId,
                            OptionText = o.OptionText
                        }).ToList(),
                        SelectedAnswer = answer == null ? null : new VendorAnswerDto
                        {
                            SelectedOptionId = answer.SelectedOptionId,
                            EnteredValue = answer.EnteredValue,
                            ScoreAchieved = answer.ScoreAchieved
                        }
                    };
                }).ToList()
            }).ToList();

            return Ok(result);
        }


        [HttpPost("SaveCategoryComment")]
        public async Task<IActionResult> SaveCategoryComment(VendorCategoryCommentDto model)
        {
            var existing = await _context.VendorCategoryComments
                .FirstOrDefaultAsync(x =>
                    x.VendorId == model.VendorId &&
                    x.CategoryId == model.CategoryId);

            if (existing == null)
            {
                var entity = new VendorCategoryComment
                {
                    VendorId = model.VendorId,
                    CategoryId = model.CategoryId,
                    Comment = model.Comment,
                    CreatedDate = DateTime.UtcNow
                };

                _context.VendorCategoryComments.Add(entity);
            }
            else
            {
                existing.Comment = model.Comment;
                existing.UpdatedDate = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return Ok("Category comment saved successfully.");
        }

        // ✅ Get Comments by VendorId
        [HttpGet("GetCategoryCommentByVendor/{vendorId}")]
        public async Task<IActionResult> GetByVendor(string vendorId)
        {
            var comments = await _context.VendorCategoryComments
                .AsNoTracking()
                .Where(x => x.VendorId == vendorId)
                .Select(x => new VendorCategoryCommentResponseDto
                {
                    CategoryId = x.CategoryId,
                    CategoryName = x.Category!.CategoryName,
                    Comment = x.Comment
                })
                .OrderBy(x => x.CategoryName)
                .ToListAsync();

            return Ok(comments);
        }



        [HttpPost("ImportVendorMaster")]
        [RequestSizeLimit(200_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 200_000_000)]
        public async Task<IActionResult> ImportVendorMaster(IFormFile file, string Username)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File not found");

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var tempFile = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid()}_{file.FileName}");

            try
            {
                // Copy upload → temp file (STREAMING)
                using (var fs = new FileStream(
                    tempFile,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    useAsync: true))
                {
                    await file.CopyToAsync(fs);
                }

                // 2️⃣ Get connection from DbContext
                var conn = (NpgsqlConnection)_context.Database.GetDbConnection();

                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync();

                // 2️⃣ Open seekable stream
                using var excelStream = new FileStream(
                    tempFile,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    useAsync: false);

                using var reader = ExcelReaderFactory.CreateReader(excelStream);

                // do
                // {
                try
                {

                    string targetTable = "vendor_master";

                    // Read table columns
                    var tableColumns = new List<string>();
                    const string columnsSql = @"
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @table
                ORDER BY ordinal_position;";

                    using (var cmd = new NpgsqlCommand(columnsSql, conn))
                    {
                        cmd.Parameters.AddWithValue("table", targetTable);
                        using var colReader = cmd.ExecuteReader();
                        while (colReader.Read())
                            tableColumns.Add(colReader.GetString(0));
                    }

                    // Create temp table
                    using (var cmd = new NpgsqlCommand(
                        $"CREATE TEMP TABLE {targetTable}_temp (LIKE {targetTable})", conn))
                    {
                        cmd.ExecuteNonQuery();
                    }

                    reader.Read(); // header row

                    // COPY streaming
                    using (var writer = conn.BeginTextImport(
                        $"COPY {targetTable}_temp ({string.Join(",", tableColumns)}) FROM STDIN WITH (FORMAT csv)"))
                    {
                        while (reader.Read())
                        {
                            var line = new StringBuilder();

                            for (int i = 0; i < tableColumns.Count; i++)
                            {
                                if (i > 0) line.Append(',');

                                var cell = reader.GetValue(i);

                                if (i == 0)
                                {
                                    var value = EscapeCsv(NormalizeValue(cell));
                                    if (string.IsNullOrEmpty(value))
                                    {
                                        continue;
                                    }
                                }
                                line.Append(EscapeCsv(NormalizeValue(cell)));
                            }

                            writer.WriteLine(line.ToString());
                        }
                    }

                    using (var cmd = new NpgsqlCommand(
                        $"TRUNCATE TABLE {targetTable} RESTART IDENTITY;", conn))
                    {
                        cmd.ExecuteNonQuery();
                    }

                    using (var cmd = new NpgsqlCommand($@"
                        INSERT INTO {targetTable} ({string.Join(",", tableColumns)})
                        SELECT {string.Join(",", tableColumns)}
                        FROM {targetTable}_temp;", conn))
                    {
                        cmd.ExecuteNonQuery();
                    }

                    using (var cmd = new NpgsqlCommand(
                        $"DROP TABLE IF EXISTS {targetTable}_temp;", conn))
                    {
                        cmd.ExecuteNonQuery();
                    }

                    //break;

                }
                catch (Exception ex)
                {
                    throw new Exception("Error during import: " + ex.Message, ex);
                }

                // } while (reader);

                return Ok("Import completed successfully");
            }
            finally
            {
                // 3️⃣ Cleanup temp file
                if (System.IO.File.Exists(tempFile))
                    System.IO.File.Delete(tempFile);
            }
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
        public string NormalizeValue(object cell)
        {
            if (cell == null)
                return "";

            // Excel dates often come as DateTime already
            if (cell is DateTime dt)
                return dt.ToString("yyyy-MM-dd HH:mm:ss");

            var text = cell.ToString()?.Trim();
            if (string.IsNullOrEmpty(text))
                return "";

            // Handle dd-MM-yyyy or dd-MM-yyyy HH:mm:ss
            if (DateTime.TryParseExact(
                    text,
                    new[] {
                "dd-MM-yyyy",
                "dd-MM-yyyy HH:mm:ss",
                "dd/MM/yyyy",
                "dd/MM/yyyy HH:mm:ss"
                    },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                return parsed.ToString("yyyy-MM-dd HH:mm:ss");
            }

            // Pass through everything else
            return text;
        }

        [HttpPost("ExportVendorRating")]
        public async Task<IActionResult> ExportVendorRating(string vendorId)
        {

            List<VendorQuestionCategoryDto> vendorData;
            List<VendorCategoryCommentResponseDto> vendorComments;
            IWorkbook workbook = new XSSFWorkbook();


            // Load all categories
            var categories = await _context.QuestionCategories
                .Include(c => c.Questions)
                    .ThenInclude(q => q.Options)
                .ToListAsync();

            // Load all answers for vendor
            var answers = await _context.VendorQuestionAnswers
                .Where(a => a.VendorId == vendorId)
                .ToListAsync();

            // Build the final DTO
            vendorData = categories.Select(c => new VendorQuestionCategoryDto
            {
                CategoryId = c.CategoryId,
                CategoryName = c.CategoryName,
                Description = c.Description,
                //CategoryWeightage = c.Weightage,
                Questions = c.Questions.Select(q =>
                {
                    var answer = answers.FirstOrDefault(a => a.QuestionId == q.QuestionId);

                    return new VendorQuestionDto
                    {
                        QuestionId = q.QuestionId,
                        QuestionText = q.QuestionText,
                        Options = q.Options.Select(o => new QuestionOptionDtoV1
                        {
                            OptionId = o.OptionId,
                            OptionText = o.OptionText
                        }).ToList(),
                        SelectedAnswer = answer == null ? null : new VendorAnswerDto
                        {
                            SelectedOptionId = answer.SelectedOptionId,
                            EnteredValue = answer.EnteredValue,
                            ScoreAchieved = answer.ScoreAchieved
                        }
                    };
                }).ToList()
            }).ToList();
            //--------------------------------------------------
            // SHEET 1 : Vendor Rating
            //--------------------------------------------------
            ISheet sheet1 = workbook.CreateSheet("Vendor Rating");

            // Header
            var headerRow = sheet1.CreateRow(0);
            headerRow.CreateCell(0).SetCellValue("Category");
            headerRow.CreateCell(1).SetCellValue("Question");
            headerRow.CreateCell(2).SetCellValue("Selected Option");
            headerRow.CreateCell(3).SetCellValue("Entered Value");
            headerRow.CreateCell(4).SetCellValue("Score Achieved");

            int rowIndex = 1;

            foreach (var category in vendorData)
            {
                foreach (var question in category.Questions)
                {
                    var row = sheet1.CreateRow(rowIndex++);

                    row.CreateCell(0).SetCellValue(category.CategoryName);
                    row.CreateCell(1).SetCellValue(question.QuestionText);
                    row.CreateCell(2).SetCellValue(
                        question.SelectedAnswer?.SelectedOptionId.ToString());
                    row.CreateCell(3).SetCellValue(
                        question.SelectedAnswer?.EnteredValue);
                    row.CreateCell(4).SetCellValue(
                        (double)(question.SelectedAnswer?.ScoreAchieved ?? 0));
                }
            }

            //for (int i = 0; i <= 4; i++)
            //    sheet1.AutoSizeColumn(i);

            //--------------------------------------------------
            // SHEET 2 : Another Response
            //--------------------------------------------------

            vendorComments = await _context.VendorCategoryComments
                .AsNoTracking()
                .Where(x => x.VendorId == vendorId)
                .Select(x => new VendorCategoryCommentResponseDto
                {
                    CategoryId = x.CategoryId,
                    CategoryName = x.Category!.CategoryName,
                    Comment = x.Comment
                })
                .OrderBy(x => x.CategoryName)
                .ToListAsync();
            ISheet sheet2 = workbook.CreateSheet("CategoryComment");

            var header2 = sheet2.CreateRow(0);
            header2.CreateCell(0).SetCellValue("CategoryId");
            header2.CreateCell(1).SetCellValue("CategoryName");
            header2.CreateCell(2).SetCellValue("Comment");

            int rowIndex2 = 1;

            foreach (var item in vendorComments)
            {
                var row = sheet2.CreateRow(rowIndex2++);

                row.CreateCell(0).SetCellValue(item.CategoryId);
                row.CreateCell(1).SetCellValue(item.CategoryName);
                row.CreateCell(2).SetCellValue(item.Comment);
            }

            for (int i = 0; i <= 2; i++)
                sheet2.AutoSizeColumn(i);

            //--------------------------------------------------
            // Return File
            //--------------------------------------------------
            using var stream = new MemoryStream();
            workbook.Write(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "VendorRating.xlsx");
        }


        [HttpPost("export-vendor-excel")]
        public async Task<IActionResult> ExportVendorExcel([FromBody] VendorDownloadRequestDto request)
        {
            if (request.VendorIds == null || !request.VendorIds.Any())
                return BadRequest("VendorIds required");

            var ratingLookup = new Dictionary<string, int>
{
    { "FULL", 7 }, { "MOST", 6 }, { "SOME", 5 }, { "BARELY", 4 }, { "DNM", 3 }
};

            // 1. Fetch Data
            var comments = await _context.VendorCategoryComments
                .Where(x => request.VendorIds.Contains(x.VendorId))
                .Select(x => new { x.VendorId, CategoryName = x.Category!.CategoryName, x.Comment })
                .ToListAsync();

            var categories = await _context.QuestionCategories
                .Include(c => c.Questions)
                .ThenInclude(q => q.Options)
                .ToListAsync();

            var answers = await _context.VendorQuestionAnswers
                .Where(a => request.VendorIds.Contains(a.VendorId))
                .ToListAsync();

            // 2. Create Workbook
            IWorkbook workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("Vendor Export");

            // Text Style (for VendorId)
            //ICellStyle textStyle = workbook.CreateCellStyle();
            //textStyle.DataFormat = workbook.CreateDataFormat().GetFormat("@");

            // 3. Create Header Row
            IRow headerRow = sheet.CreateRow(0);

            string[] headers = { "VendorId", "Category", "Question", "Answer", "Score", "Comment" };

            for (int i = 0; i < headers.Length; i++)
            {
                ICell cell = headerRow.CreateCell(i);
                cell.SetCellValue(headers[i]);
                //cell.CellStyle = headerStyle;
            }

            int currentRow = 1;

            // 4. Populate Rows
            foreach (var vendorId in request.VendorIds)
            {
                foreach (var category in categories)
                {
                    foreach (var question in category.Questions)
                    {
                        var answer = answers.FirstOrDefault(a =>
                            a.VendorId == vendorId && a.QuestionId == question.QuestionId);

                        var comment = comments.FirstOrDefault(c =>
                            c.VendorId == vendorId && c.CategoryName == category.CategoryName);

                        string selectedText = "";
                        int score = 0;

                        if (answer != null)
                        {
                            var option = question.Options
                                .FirstOrDefault(o => o.OptionId == answer.SelectedOptionId);

                            if (option != null)
                            {
                                selectedText = option.OptionText;

                                if (!string.IsNullOrEmpty(selectedText))
                                    ratingLookup.TryGetValue(selectedText, out score);
                            }
                        }

                        IRow row = sheet.CreateRow(currentRow);

                        // VendorId as TEXT
                        var vendorCell = row.CreateCell(0);
                        vendorCell.SetCellValue(vendorId);
                        //vendorCell.CellStyle = textStyle;

                        row.CreateCell(1).SetCellValue(category.CategoryName);
                        row.CreateCell(2).SetCellValue(question.QuestionText);
                        row.CreateCell(3).SetCellValue(selectedText);
                        row.CreateCell(4).SetCellValue(score);
                        row.CreateCell(5).SetCellValue(comment?.Comment ?? "");

                        currentRow++;
                    }
                }
            }

            // 5. Auto-size columns
            for (int i = 0; i < headers.Length; i++)
            {
                //sheet.AutoSizeColumn(i);
            }

            // 6. Return File
            using (var stream = new MemoryStream())
            {
                workbook.Write(stream);
                var content = stream.ToArray();

                return File(
                    content,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"VendorExport_{DateTime.Now:yyyyMMddHHmmss}.xlsx"
                );
            }
        }

    }
}
