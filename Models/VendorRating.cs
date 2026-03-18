using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
namespace TimeSheet.Models
{


    [Table("question_category_master")]
    public class QuestionCategoryMaster
    {
        [Key]
        [Column("category_id")]
        public long CategoryId { get; set; }

        [Required]
        [MaxLength(255)]
        [Column("category_name")]
        public string CategoryName { get; set; }

        [MaxLength(500)]
        [Column("description")]
        public string? Description { get; set; }

        [Required]
        [Column("vendor_type_id")]
        public long VendorTypeId { get; set; }

        [Required]
        [Column("category_weightage", TypeName = "numeric(5,2)")]
        public decimal CategoryWeightage { get; set; }

        [Required]
        [Column("active_flag")]
        public bool ActiveFlag { get; set; } = true;

        [Required]
        [Column("created_by")]
        public long CreatedBy { get; set; }

        [Required]
        [Column("created_date")]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public ICollection<QuestionMaster>? Questions { get; set; }
    }

    public enum QuestionType
    {
        MCQ,
        RATING,
        YESNO,
        TEXT
    }

    [Table("question_master")]
    public class QuestionMaster
    {
        [Key]
        [Column("question_id")]
        public long QuestionId { get; set; }

        [Required]
        [Column("category_id")]
        public long CategoryId { get; set; }

        [ForeignKey("CategoryId")]
        //[JsonIgnore]
        public QuestionCategoryMaster? Category { get; set; }

        [Required]
        [Column("question_text")]
        public string QuestionText { get; set; }

        [Required]
        [Column("question_type")]
        public QuestionType QuestionType { get; set; }

        [Required]
        [Column("weightage", TypeName = "numeric(5,2)")]
        public decimal Weightage { get; set; }

        [Required]
        [Column("mandatory_flag")]
        public bool MandatoryFlag { get; set; } = false;

        [Required]
        [Column("vendor_type_id")]
        public long VendorTypeId { get; set; }

        [Required]
        [Column("active_flag")]
        public bool ActiveFlag { get; set; } = true;

        [Required]
        [Column("version_no")]
        public int SequenceNo { get; set; } = 1;

        [Required]
        [Column("created_by")]
        public long CreatedBy { get; set; }

        [Required]
        [Column("created_date")]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public ICollection<QuestionOptionMaster>? Options { get; set; }
    }
    [Table("question_option_master")]
    public class QuestionOptionMaster
    {
        [Key]
        [Column("option_id")]
        public long OptionId { get; set; }

        [Required]
        [Column("question_id")]
        public long QuestionId { get; set; }

        [ForeignKey("QuestionId")]
        public QuestionMaster? Question { get; set; }

        [Required]
        [MaxLength(255)]
        [Column("option_text")]
        public string OptionText { get; set; }

        [Required]
        [Column("score_value", TypeName = "numeric(5,2)")]
        public decimal ScoreValue { get; set; }

        [Required]
        [Column("active_flag")]
        public bool ActiveFlag { get; set; } = true;
    }

    public class CategoryWithQuestionsDto
    {
        public long CategoryId { get; set; }
        public string CategoryName { get; set; }
        public string? Description { get; set; }
        public decimal CategoryWeightage { get; set; }
        public List<QuestionDto> Questions { get; set; }
    }

    public class QuestionDto
    {
        public long QuestionId { get; set; }
        public string QuestionText { get; set; }

        public bool MandatoryFlag { get; set; } = false;
        public bool ActiveFlag { get; set; } = true;
        public int SequenceNo { get; set; } = 1;
        public List<QuestionOptionDto> Options { get; set; }
    }

    public class QuestionOptionDto
    {
        public long OptionId { get; set; }
        public string OptionText { get; set; }
    }

    [Table("vendor_evaluation", Schema = "public")]
    public class VendorEvaluation
    {
        [Key]
        [Column("evaluation_id")]
        public long EvaluationId { get; set; }

        [Required]
        [Column("vendor_id")]
        public long VendorId { get; set; }

        [Required]
        [Column("vendor_type_id")]
        public long VendorTypeId { get; set; }

        [Required]
        [Column("evaluation_date")]
        public DateTime EvaluationDate { get; set; } = DateTime.UtcNow;

        [Column("total_score", TypeName = "numeric(10,2)")]
        public decimal? TotalScore { get; set; }

        [Column("rating_code")]
        [StringLength(10)]
        public string? RatingCode { get; set; }

        [Column("status")]
        [StringLength(30)]
        public string Status { get; set; } = "DRAFT";

        [Column("submitted_by")]
        public long? SubmittedBy { get; set; }

        [Column("approved_by")]
        public long? ApprovedBy { get; set; }

        [Column("approved_date")]
        public DateTime? ApprovedDate { get; set; }

        [Column("created_date")]
        public DateTime? CreatedDate { get; set; } = DateTime.UtcNow;
    }

    public class VendorDto
    {
        public string? VendorId { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }

    }
    [Table("vendor_master")]
    public class Vendor
    {
        [Column("vendor_id")]
        public string? VendorId { get; set; }

        [Column("name")]
        public string? Name { get; set; }
        [Column("type")]
        public string? Type { get; set; }
    }

    [Table("vendor_question_answer", Schema = "public")]
    public class VendorQuestionAnswer
    {
        [Key]
        [Column("answer_id")]
        public long AnswerId { get; set; }

        [Required]
        [Column("vendor_id")]
        public string VendorId { get; set; }

        [Required]
        [Column("question_id")]
        public long QuestionId { get; set; }

        [Column("selected_option_id")]
        public long? SelectedOptionId { get; set; }

        [Column("entered_value")]
        public string? EnteredValue { get; set; }

        [Column("score_achieved", TypeName = "numeric(10,2)")]
        public decimal? ScoreAchieved { get; set; }

        [Required]
        [Column("weightage_snapshot", TypeName = "numeric(5,2)")]
        public decimal WeightageSnapshot { get; set; }

        [Column("created_date")]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        // Navigation properties (optional if you want EF to load related data)
        // public QuestionMaster Question { get; set; }
        // public QuestionOptionMaster SelectedOption { get; set; }
    }
    [Table("vendor_category_comments")]
    public class VendorCategoryComment
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("vendor_id")]
        public string VendorId { get; set; } = null!;

        [Column("category_id")]
        public long CategoryId { get; set; }

        [Column("comment")]
        public string? Comment { get; set; }

        [Column("created_date")]
        public DateTime CreatedDate { get; set; }

        [Column("updated_date")]
        public DateTime? UpdatedDate { get; set; }

        // Navigation Property
        public QuestionCategoryMaster? Category { get; set; }
    }

    ///////////////////////////////////////
    /// <summary>
    ///
    /// </summary>

    public class VendorCategoryCommentResponseDto
    {
        public long CategoryId { get; set; }
        public string CategoryName { get; set; } = null!;
        public string? Comment { get; set; }
    }
    public class VendorCategoryCommentDto
    {
        public string VendorId { get; set; } = null!;
        public long CategoryId { get; set; }
        public string? Comment { get; set; }
    }
    public class VendorQuestionCategoryDto
    {
        public long CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal CategoryWeightage { get; set; }
        public List<VendorQuestionDto> Questions { get; set; } = new();
    }

    public class VendorQuestionDto
    {
        public long QuestionId { get; set; }
        public long SequenceNo { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public List<QuestionOptionDtoV1> Options { get; set; } = new();
        public VendorAnswerDto? SelectedAnswer { get; set; } // Selected option if answered
    }

    public class QuestionOptionDtoV1
    {
        public long OptionId { get; set; }
        public string OptionText { get; set; } = string.Empty;
    }

    public class VendorAnswerDto
    {
        public long? SelectedOptionId { get; set; }
        public string? EnteredValue { get; set; }
        public decimal? ScoreAchieved { get; set; }
    }
    public class VendorDownloadRequestDto
    {
        public List<string> VendorIds { get; set; } = new List<string>();
    }
}
