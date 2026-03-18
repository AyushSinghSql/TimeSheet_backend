namespace TimeSheet.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Text.Json.Serialization;
    using static TimeSheet.DTOs.UserMappingExtensions;

    [Table("timesheet", Schema = "public")]
    public class Timesheet
    {
        [Key]
        [Column("timesheet_id")]
        public int TimesheetId { get; set; }

        [Column("timesheet_date")]
        public DateOnly? TimesheetDate { get; set; }

        [Column("employee_id")]
        public string EmployeeId { get; set; } = string.Empty;

        [Column("timesheet_type_code")]
        public string? TimesheetTypeCode { get; set; }

        [Column("working_state")]
        public string? WorkingState { get; set; }

        [Column("fiscal_year")]
        public int FiscalYear { get; set; }

        [Column("period")]
        public int Period { get; set; }

        [Column("subperiod")]
        public int? Subperiod { get; set; }

        [Column("correcting_ref_date")]
        public DateOnly? CorrectingRefDate { get; set; }

        [Column("pay_type")]
        public string? PayType { get; set; }

        [Column("general_labor_category")]
        public string? GeneralLaborCategory { get; set; }

        [Column("timesheet_line_type_code")]
        public string? TimesheetLineTypeCode { get; set; }

        [Column("labor_cost_amount")]
        public decimal? LaborCostAmount { get; set; }

        [Column("hours")]
        public decimal? Hours { get; set; }

        [Column("workers_comp_code")]
        public string? WorkersCompCode { get; set; }

        [Column("labor_location_code")]
        public string? LaborLocationCode { get; set; }

        [Column("organization_id")]
        public string? OrganizationId { get; set; }

        [Column("account_id")]
        public string? AccountId { get; set; }

        [Column("project_id")]
        public string? ProjectId { get; set; }

        [Column("project_labor_category")]
        public string? ProjectLaborCategory { get; set; }

        [Column("reference_number_1")]
        public string? ReferenceNumber1 { get; set; }

        [Column("reference_number_2")]
        public string? ReferenceNumber2 { get; set; }

        [Column("organization_abbreviation")]
        public string? OrganizationAbbreviation { get; set; }

        [Column("project_abbreviation")]
        public string? ProjectAbbreviation { get; set; }

        [Column("sequence_number")]
        public int? SequenceNumber { get; set; }

        [Column("effective_billing_date")]
        public DateOnly? EffectiveBillingDate { get; set; }

        [Column("project_account_abbrev")]
        public string? ProjectAccountAbbrev { get; set; }

        [Column("multi_state_code")]
        public string? MultiStateCode { get; set; }

        [Column("reference_sequence_num")]
        public int? ReferenceSequenceNum { get; set; }

        [Column("timesheet_line_date")]
        public DateOnly? TimesheetLineDate { get; set; }

        [Column("notes")]
        public string? Notes { get; set; }

        // Audit fields
        [Column("created_date")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [Column("modified_date")]
        public DateTime ModifiedDate { get; set; } = DateTime.Now;

        [Column("created_by")]
        public string CreatedBy { get; set; } = "system";

        [Column("modified_by")]
        public string? ModifiedBy { get; set; }

        [Column("rowversion")]
        public long RowVersion { get; set; }
        [Column("batch_id")]
        public string? BatchId { get; set; }

        [NotMapped]
        public int RequestId { get; set; } = 0;
        [NotMapped]
        public string? ApprovalStatus { get; set; }
        [NotMapped]
        public string DisplayedName { get; set; }
        [NotMapped]
        public string? Comment { get; set; }
        [NotMapped]
        public string? IPAddress { get; set; }
        [NotMapped]
        public string? ApprovedBy { get; set; }
        [NotMapped]
        public string? Status { get; set; }
        [NotMapped]
        public string? ApproverId { get; set; }
        [JsonIgnore]
        public ICollection<ApprovalRequest> ApprovalRequests { get; set; } = new List<ApprovalRequest>();
        //[JsonIgnore]
        [NotMapped]
        public ICollection<ApprovalAction> ApprovalActions { get; set; } = new List<ApprovalAction>();

        [JsonIgnore]
        public Employee? Employee { get; set; } = null!;
        [NotMapped]
        public string? ApprovedDate { get; internal set; }
        [NotMapped]
        public bool? IsExported { get; internal set; }
        [NotMapped]
        public string? ImportedTimestamp { get; set; }

    }

    public class TimesheetArchive
    {
        public long TimesheetId { get; set; }
        public DateOnly? TimesheetDate { get; set; }
        public string EmployeeId { get; set; }
        public string TimesheetTypeCode { get; set; }
        public string WorkingState { get; set; }
        public int? FiscalYear { get; set; }
        public int? Period { get; set; }
        public int? Subperiod { get; set; }
        public DateOnly? CorrectingRefDate { get; set; }
        public string PayType { get; set; }
        public string GeneralLaborCategory { get; set; }
        public string TimesheetLineTypeCode { get; set; }
        public decimal? LaborCostAmount { get; set; }
        public decimal? Hours { get; set; }
        public string WorkersCompCode { get; set; }
        public string Status { get; set; }
        public string LaborLocationCode { get; set; }
        public string OrganizationId { get; set; }
        public string AccountId { get; set; }
        public string ProjectId { get; set; }
        public string ProjectLaborCategory { get; set; }
        public string ReferenceNumber1 { get; set; }
        public string ReferenceNumber2 { get; set; }
        public string OrganizationAbbreviation { get; set; }
        public string ProjectAbbreviation { get; set; }
        public int? SequenceNumber { get; set; }
        public DateOnly? EffectiveBillingDate { get; set; }
        public string ProjectAccountAbbrev { get; set; }
        public string MultiStateCode { get; set; }
        public int? ReferenceSequenceNum { get; set; }
        public DateOnly? TimesheetLineDate { get; set; }
        public string Notes { get; set; }
        public DateTime DeletedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string DeletedBy { get; set; }
        public string ModifiedBy { get; set; }
        public long Rowversion { get; set; }
        public string BatchId { get; set; }
    }

    public class TimesheetNotifyDTO
    {
        public string DisplayedName { get; set; }
        public string? BatchId { get; set; }
        public string? EmployeeId { get; set; }
        public string? ProjectId { get; set; }
        public LevelDetailsDto User { get; set; }

        public DateOnly? TimesheetDate { get; set; }
    }

}
