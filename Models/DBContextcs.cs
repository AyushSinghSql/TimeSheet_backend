using System.Collections.Generic;
using System.Reflection.Emit;
using Microsoft.EntityFrameworkCore;
using TimeSheet.Models.YourNamespace.Models;

namespace TimeSheet.Models
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<ApprovalWorkflow> ApprovalWorkflows { get; set; }
        public DbSet<ApprovalApprover> ApprovalApprovers { get; set; }
        public DbSet<ApprovalRequest> ApprovalRequests { get; set; }
        public DbSet<ApprovalAction> ApprovalActions { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<Employee> Employees { get; set; }

        public DbSet<Timesheet> Timesheets { get; set; }
        public DbSet<ConfigValue> ConfigValues { get; set; }
        public DbSet<TimesheetArchive> TimesheetArchives { get; set; }
        public DbSet<QuestionCategoryMaster> QuestionCategories { get; set; }
        public DbSet<QuestionMaster> Questions { get; set; }
        public DbSet<QuestionOptionMaster> Options { get; set; }
        public DbSet<VendorEvaluation> VendorEvaluations { get; set; }
        public DbSet<Vendor> Vendors { get; set; }
        public DbSet<VendorQuestionAnswer> VendorQuestionAnswers { get; set; }
        public DbSet<VendorCategoryComment> VendorCategoryComments { get; set; }
        public DbSet<UserBackup> UserBackups { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            AppContext.SetSwitch("Npgsql.DisableDateTimeInfinityConversions", true);

            modelBuilder.Entity<UserBackup>()
               .HasKey(ub => new { ub.UserId, ub.BackupUserId });

            modelBuilder.Entity<UserBackup>()
                .HasOne(ub => ub.User)
                .WithMany(u => u.Backups)
                .HasForeignKey(ub => ub.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserBackup>()
                .HasOne(ub => ub.BackupUser)
                .WithMany()
                .HasForeignKey(ub => ub.BackupUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VendorCategoryComment>()
                .HasOne(v => v.Category)
                .WithMany()
                .HasForeignKey(v => v.CategoryId);

            modelBuilder.Entity<VendorCategoryComment>()
                .HasIndex(v => new { v.VendorId, v.CategoryId })
                .IsUnique();

            modelBuilder.Entity<VendorQuestionAnswer>()
                .Property(v => v.AnswerId)
                .ValueGeneratedOnAdd();

            // Optional: configure foreign keys if you want EF navigation
            modelBuilder.Entity<VendorQuestionAnswer>()
                .HasOne<QuestionMaster>() // Assuming you have QuestionMaster entity
                .WithMany()
                .HasForeignKey(v => v.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VendorQuestionAnswer>()
                .HasOne<QuestionOptionMaster>() // Assuming you have QuestionOptionMaster entity
                .WithMany()
                .HasForeignKey(v => v.SelectedOptionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Vendor>();

            modelBuilder.Entity<VendorEvaluation>()
                .Property(v => v.EvaluationId)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<QuestionOptionMaster>()
                .Property(o => o.ScoreValue)
                .HasPrecision(5, 2);

            modelBuilder.Entity<QuestionOptionMaster>()
                .HasIndex(o => o.QuestionId)
                .HasDatabaseName("ix_qom_question");

            modelBuilder.Entity<QuestionCategoryMaster>()
                .HasIndex(q => new { q.CategoryName, q.VendorTypeId })
                .IsUnique()
                .HasDatabaseName("uq_category");

            modelBuilder.Entity<QuestionCategoryMaster>()
                .Property(q => q.CategoryWeightage)
                .HasPrecision(5, 2);

            modelBuilder.Entity<QuestionMaster>()
                .HasOne(q => q.Category)
                .WithMany(c => c.Questions)
                .HasForeignKey(q => q.CategoryId) // <-- explicitly map to CategoryId
                .HasConstraintName("fk_question_category");

            modelBuilder.Entity<QuestionMaster>()
                .Property(q => q.Weightage)
                .HasPrecision(5, 2);

            modelBuilder.Entity<QuestionMaster>()
                .HasCheckConstraint("question_master_weightage_check",
                    "weightage >= 0");

            modelBuilder.Entity<QuestionMaster>()
                .HasCheckConstraint("question_master_question_type_check",
                    "question_type IN ('MCQ','RATING','YESNO','TEXT')");

            // Store enum as string in DB
            modelBuilder.Entity<QuestionMaster>()
                .Property(q => q.QuestionType)
                .HasConversion<string>();


            modelBuilder.Entity<TimesheetArchive>(entity =>
            {
                entity.ToTable("timesheet_archive");

                entity.HasKey(e => e.TimesheetId)
                      .HasName("timesheet_archive_pkey");

                entity.Property(e => e.TimesheetId).HasColumnName("timesheet_id");
                entity.Property(e => e.TimesheetDate).HasColumnName("timesheet_date");
                entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
                entity.Property(e => e.TimesheetTypeCode).HasColumnName("timesheet_type_code");
                entity.Property(e => e.WorkingState).HasColumnName("working_state");
                entity.Property(e => e.FiscalYear).HasColumnName("fiscal_year");
                entity.Property(e => e.Period).HasColumnName("period");
                entity.Property(e => e.Subperiod).HasColumnName("subperiod");
                entity.Property(e => e.CorrectingRefDate).HasColumnName("correcting_ref_date");
                entity.Property(e => e.PayType).HasColumnName("pay_type");
                entity.Property(e => e.GeneralLaborCategory).HasColumnName("general_labor_category");
                entity.Property(e => e.TimesheetLineTypeCode).HasColumnName("timesheet_line_type_code");
                entity.Property(e => e.LaborCostAmount).HasColumnName("labor_cost_amount");
                entity.Property(e => e.Hours).HasColumnName("hours");
                entity.Property(e => e.WorkersCompCode).HasColumnName("workers_comp_code");
                entity.Property(e => e.Status).HasColumnName("status");
                entity.Property(e => e.LaborLocationCode).HasColumnName("labor_location_code");
                entity.Property(e => e.OrganizationId).HasColumnName("organization_id");
                entity.Property(e => e.AccountId).HasColumnName("account_id");
                entity.Property(e => e.ProjectId).HasColumnName("project_id");
                entity.Property(e => e.ProjectLaborCategory).HasColumnName("project_labor_category");
                entity.Property(e => e.ReferenceNumber1).HasColumnName("reference_number_1");
                entity.Property(e => e.ReferenceNumber2).HasColumnName("reference_number_2");
                entity.Property(e => e.OrganizationAbbreviation).HasColumnName("organization_abbreviation");
                entity.Property(e => e.ProjectAbbreviation).HasColumnName("project_abbreviation");
                entity.Property(e => e.SequenceNumber).HasColumnName("sequence_number");
                entity.Property(e => e.EffectiveBillingDate).HasColumnName("effective_billing_date");
                entity.Property(e => e.ProjectAccountAbbrev).HasColumnName("project_account_abbrev");
                entity.Property(e => e.MultiStateCode).HasColumnName("multi_state_code");
                entity.Property(e => e.ReferenceSequenceNum).HasColumnName("reference_sequence_num");
                entity.Property(e => e.TimesheetLineDate).HasColumnName("timesheet_line_date");
                entity.Property(e => e.Notes).HasColumnName("notes");
                entity.Property(e => e.DeletedDate).HasColumnName("deleted_date");
                entity.Property(e => e.ModifiedDate).HasColumnName("modified_date");
                entity.Property(e => e.DeletedBy).HasColumnName("deleted_by");
                entity.Property(e => e.ModifiedBy).HasColumnName("modified_by");
                entity.Property(e => e.Rowversion).HasColumnName("rowversion");
                entity.Property(e => e.BatchId).HasColumnName("batch_id");
            });


            modelBuilder.Entity<ConfigValue>(entity =>
            {
                entity.ToTable("config_values");

                entity.Property(e => e.Id)
                      .HasColumnName("id")
                      .ValueGeneratedOnAdd();

                entity.Property(e => e.Name)
                      .HasColumnName("name")
                      .HasMaxLength(20)
                      .IsRequired();

                entity.Property(e => e.Value)
                      .HasColumnName("value")
                      .HasMaxLength(20);

                entity.Property(e => e.CreatedAt)
                      .HasColumnType("timestamp without time zone")
                      .HasColumnName("created_at");

            });

            modelBuilder.Entity<Timesheet>(entity =>
            {
                entity.HasKey(t => t.TimesheetId);


                entity.HasOne(t => t.Employee)
                        .WithMany(e => e.Timesheets)
                        .HasForeignKey(t => t.EmployeeId)
                        .HasConstraintName("timesheet_employee_fk");
            });

            // USERS
            modelBuilder.Entity<User>().ToTable("users").HasKey(u => u.UserId);
            modelBuilder.Entity<User>(entity =>
            {
                entity.Property(u => u.UserId).HasColumnName("user_id");
                entity.Property(u => u.Username).HasColumnName("username");
                entity.Property(u => u.FullName).HasColumnName("full_name");
                entity.Property(u => u.Email).HasColumnName("email");
                entity.Property(u => u.IsActive).HasColumnName("is_active");
                entity.Property(u => u.PasswordHash).HasColumnName("password_hash");
                entity.Property(u => u.Role).HasColumnName("role");
                entity.Property(u => u.FirstLogin).HasColumnName("first_login");
                entity.Property(u => u.CreatedAt).HasColumnName("created_at");
            });

            // APPROVAL WORKFLOW
            modelBuilder.Entity<ApprovalWorkflow>().ToTable("approval_workflow").HasKey(w => w.WorkflowId);
            modelBuilder.Entity<ApprovalWorkflow>(entity =>
            {
                entity.Property(w => w.WorkflowId).HasColumnName("workflow_id");
                entity.Property(w => w.RequestType).HasColumnName("request_type");
                entity.Property(w => w.LevelNo).HasColumnName("level_no");
                entity.Property(w => w.ApproverRole).HasColumnName("level_name");
                entity.Property(w => w.IsMandetory).HasColumnName("is_mandetory");
            });

            // APPROVAL APPROVER
            modelBuilder.Entity<ApprovalApprover>().ToTable("approval_approver").HasKey(a => a.ApproverId);
            modelBuilder.Entity<ApprovalApprover>(entity =>
            {
                entity.Property(a => a.ApproverId).HasColumnName("approver_id");
                entity.Property(a => a.WorkflowId).HasColumnName("workflow_id");
                entity.Property(a => a.UserId).HasColumnName("user_id");
                entity.Property(a => a.IsActive).HasColumnName("is_active");
            });

            // APPROVAL REQUEST
            modelBuilder.Entity<ApprovalRequest>().ToTable("approval_request").HasKey(r => r.RequestId);
            modelBuilder.Entity<ApprovalRequest>(entity =>
            {
                entity.Property(r => r.RequestId).HasColumnName("request_id");

                entity.HasKey(r => r.RequestId).HasName("pk_approval_request");
                entity.Property(r => r.RequestType).HasColumnName("request_type");
                entity.Property(r => r.TimesheetId).HasColumnName("timesheet_id");
                entity.Property(r => r.RequesterId).HasColumnName("requester_id");
                entity.Property(r => r.RequestData).HasColumnName("request_data");
                entity.Property(r => r.Status).HasColumnName("status");
                entity.Property(r => r.IsExported).HasColumnName("isexported");
                entity.Property(r => r.CurrentLevelNo).HasColumnName("current_level_no");
                entity.Property(r => r.CreatedAt).HasColumnName("created_at");

                // FK: ApprovalRequest → Users (Requester)
                entity.HasOne(r => r.Requester) // If you add a navigation like r.Requester, use .HasOne(r => r.Requester)
                      .WithMany(t => t.ApprovalRequests)
                      .HasForeignKey(r => r.RequesterId)
                      .OnDelete(DeleteBehavior.Cascade)
                      .HasConstraintName("approval_request_requester_id_fkey");

                //// FK: ApprovalRequest → ApprovalActions (1-to-many)
                //entity.HasMany(r => r.Actions)
                //      .WithOne(a => a.Request)
                //      .HasForeignKey(a => a.RequestId)
                //      .OnDelete(DeleteBehavior.Cascade)
                //      .HasConstraintName("dct_forecast_dct_id_fkey");

                entity.HasOne(ar => ar.Timesheet)
                      .WithMany(t => t.ApprovalRequests)
                      .HasForeignKey(ar => ar.TimesheetId)
                      .OnDelete(DeleteBehavior.Cascade)
                      .HasConstraintName("approval_request_timesheet_id_fkey");
            });

            // APPROVAL ACTION
            modelBuilder.Entity<ApprovalAction>().ToTable("approval_action").HasKey(ac => ac.ActionId);
            modelBuilder.Entity<ApprovalAction>(entity =>
            {
                entity.Property(ac => ac.ActionId).HasColumnName("action_id");
                entity.Property(ac => ac.RequestId).HasColumnName("request_id");
                entity.Property(ac => ac.LevelNo).HasColumnName("level_no");
                entity.Property(ac => ac.ApproverId).HasColumnName("approver_user_id");
                entity.Property(ac => ac.ActionComment).HasColumnName("comment");
                entity.Property(ac => ac.ActionDate).HasColumnName("action_date");
                entity.Property(ac => ac.ActionStatus).HasColumnName("action");
                entity.Property(ac => ac.IpAddress).HasColumnName("ip_address");

            });

            modelBuilder.Entity<Employee>(entity =>
            {
                entity.ToTable("employee");

                entity.HasKey(e => e.EmployeeId);

                entity.Property(e => e.EmployeeId)
                      .HasMaxLength(50);

                entity.Property(e => e.DisplayedName)
                      .HasMaxLength(150)
                      .IsRequired();

                entity.Property(e => e.LastName)
                      .HasMaxLength(100);

                entity.Property(e => e.FirstName)
                      .HasMaxLength(100);

                entity.Property(e => e.Status)
                      .HasMaxLength(50);

                entity.Property(e => e.CreatedDate)
                      .HasColumnType("timestamp without time zone")
                      .HasDefaultValueSql("NOW()");

                entity.Property(e => e.ModifiedDate)
                      .HasColumnType("timestamp without time zone")
                      .HasDefaultValueSql("NOW()");


            });

            modelBuilder.Entity<Project>(entity =>
            {
                entity.ToTable("project", "public");

                entity.HasKey(e => e.ProjectId);

                entity.Property(e => e.ProjectId)
                    .HasColumnName("project_id")
                    .HasMaxLength(50);


                entity.Property(e => e.ProjectName)
                    .HasColumnName("project_name")
                    .HasMaxLength(255)
                    .IsRequired();

                entity.Property(e => e.ProjectType)
                    .HasColumnName("project_type")
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(e => e.OwningOrgId)
                    .HasColumnName("owning_org_id")
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(e => e.ProjectManagerId)
                    .HasColumnName("project_manager_id");

                entity.Property(e => e.ProjectManagerName)
                    .HasColumnName("project_manager_name")
                    .HasMaxLength(255);

                entity.Property(e => e.Email)
                    .HasColumnName("email")
                    .HasMaxLength(50);

                entity.Property(e => e.Status)
                    .HasColumnName("status")
                    .HasMaxLength(10);
            });
        }

    }

}
