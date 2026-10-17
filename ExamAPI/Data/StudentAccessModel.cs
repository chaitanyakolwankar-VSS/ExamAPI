using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Data
{
    /// <summary>Keys, indexes and relations of the student-access tables (see Models/StudentAccess.cs).</summary>
    public static class StudentAccessModel
    {
        public static void Configure(ModelBuilder modelBuilder)
        {
            // One student per email in a college (live rows only); no email yet is fine.
            modelBuilder.Entity<StudentMaster>()
                .HasIndex(s => new { s.CollegeId, s.Email })
                .IsUnique()
                .HasFilter("[Email] IS NOT NULL AND [IsDeleted] = 0");

            modelBuilder.Entity<TaskType>().HasData(
                new TaskType { Code = TaskTypes.StudentMaster, Name = "Verify student details", ExpiryRule = TaskExpiryRules.OnDeadline, RequiresOtp = true },
                new TaskType { Code = TaskTypes.HallTicket, Name = "Download hall ticket", ExpiryRule = TaskExpiryRules.OnDeadline, RequiresOtp = true },
                new TaskType { Code = TaskTypes.Marksheet, Name = "Download marksheet", ExpiryRule = TaskExpiryRules.OnDeadline, RequiresOtp = true },
                new TaskType { Code = TaskTypes.AtktPayment, Name = "Pay ATKT exam fee", ExpiryRule = TaskExpiryRules.OnPayment, RequiresOtp = true });

            // Every relation is NO ACTION: nothing here is ever hard-deleted, and SQL Server refuses
            // the several cascade paths that College -> Student -> TaskLink -> ... would create.
            modelBuilder.Entity<TaskBatch>()
                .HasOne<TaskType>().WithMany().HasForeignKey(b => b.TaskTypeCode).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<TaskBatch>()
                .HasOne<College>().WithMany().HasForeignKey(b => b.CollegeId).OnDelete(DeleteBehavior.Restrict);

            var link = modelBuilder.Entity<TaskLink>();
            link.HasOne(l => l.TaskType).WithMany().HasForeignKey(l => l.TaskTypeCode).OnDelete(DeleteBehavior.Restrict);
            link.HasOne(l => l.Batch).WithMany().HasForeignKey(l => l.BatchId).OnDelete(DeleteBehavior.Restrict);
            link.HasOne(l => l.Student).WithMany().HasForeignKey(l => l.StdMstId).OnDelete(DeleteBehavior.Restrict);
            link.HasOne<College>().WithMany().HasForeignKey(l => l.CollegeId).OnDelete(DeleteBehavior.Restrict);
            link.HasIndex(l => l.TokenHash).IsUnique().HasFilter("[TokenHash] IS NOT NULL");
            link.HasIndex(l => new { l.StdMstId, l.TaskTypeCode, l.Status });

            var otp = modelBuilder.Entity<OtpChallenge>();
            otp.HasOne(o => o.TaskLink).WithMany().HasForeignKey(o => o.TaskLinkId).OnDelete(DeleteBehavior.Restrict);
            otp.HasOne<College>().WithMany().HasForeignKey(o => o.CollegeId).OnDelete(DeleteBehavior.Restrict);
            otp.HasIndex(o => new { o.TaskLinkId, o.CreatedAt }); // "3 codes per hour per link"

            var session = modelBuilder.Entity<StudentSession>();
            session.HasOne(s => s.TaskLink).WithMany().HasForeignKey(s => s.TaskLinkId).OnDelete(DeleteBehavior.Restrict);
            session.HasOne<StudentMaster>().WithMany().HasForeignKey(s => s.StdMstId).OnDelete(DeleteBehavior.Restrict);
            session.HasOne<College>().WithMany().HasForeignKey(s => s.CollegeId).OnDelete(DeleteBehavior.Restrict);
            session.HasIndex(s => s.SessionHash).IsUnique();

            var mail = modelBuilder.Entity<EmailQueue>();
            mail.HasOne<College>().WithMany().HasForeignKey(m => m.CollegeId).OnDelete(DeleteBehavior.Restrict);
            mail.HasIndex(m => new { m.Status, m.Priority, m.NextAttemptAt }); // what the sender picks next
            mail.HasIndex(m => new { m.CollegeId, m.SentAt });                   // today's count per college

            var settings = modelBuilder.Entity<CollegeSettings>();
            settings.HasOne(s => s.College).WithMany().HasForeignKey(s => s.CollegeId).OnDelete(DeleteBehavior.Restrict);
            settings.HasIndex(s => s.CollegeId).IsUnique();
            settings.Property(s => s.DailyEmailLimit).HasDefaultValue(2000);

            // One account per college; the platform default (CollegeId null) is kept single by EmailAccountService.
            var account = modelBuilder.Entity<EmailAccount>();
            account.HasOne<College>().WithMany().HasForeignKey(a => a.CollegeId).OnDelete(DeleteBehavior.Restrict);
            account.HasIndex(a => a.CollegeId).IsUnique();
        }
    }
}
