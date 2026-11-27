using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Data
{
    /// <summary>Keys, indexes and relations of the exam-application and payment tables (Models/ExamApplications.cs).</summary>
    public static class ExamApplicationsModel
    {
        public static void Configure(ModelBuilder modelBuilder)
        {
            var setup = modelBuilder.Entity<ExamFeeSetup>();
            setup.HasOne(s => s.Exam).WithMany().HasForeignKey(s => s.ExamId).OnDelete(DeleteBehavior.Restrict);
            setup.HasOne<College>().WithMany().HasForeignKey(s => s.CollegeId).OnDelete(DeleteBehavior.Restrict);
            setup.HasMany(s => s.CategoryRates).WithOne().HasForeignKey(r => r.FeeSetupId).OnDelete(DeleteBehavior.Cascade);
            setup.HasIndex(s => new { s.CollegeId, s.ExamId }).IsUnique().HasFilter("[IsDeleted] = 0");

            var rate = modelBuilder.Entity<ExamFeeCategoryRate>();
            rate.HasOne<College>().WithMany().HasForeignKey(r => r.CollegeId).OnDelete(DeleteBehavior.Restrict);
            rate.HasIndex(r => new { r.FeeSetupId, r.Category }).IsUnique();

            var app = modelBuilder.Entity<ExamApplication>();
            app.HasOne(a => a.Student).WithMany().HasForeignKey(a => a.StdMstId).OnDelete(DeleteBehavior.Restrict);
            app.HasOne(a => a.TargetExam).WithMany().HasForeignKey(a => a.TargetExamId).OnDelete(DeleteBehavior.Restrict);
            app.HasOne<College>().WithMany().HasForeignKey(a => a.CollegeId).OnDelete(DeleteBehavior.Restrict);
            app.HasMany(a => a.Subjects).WithOne().HasForeignKey(s => s.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            // One open application per student per exam; a cancelled or refunded one does not block a new one.
            app.HasIndex(a => new { a.StdMstId, a.TargetExamId }).IsUnique()
                .HasFilter("[IsDeleted] = 0 AND [Status] NOT IN ('Cancelled', 'Refunded')");
            app.HasIndex(a => new { a.CollegeId, a.TargetExamId, a.Status });

            var subject = modelBuilder.Entity<ExamApplicationSubject>();
            subject.HasOne<College>().WithMany().HasForeignKey(s => s.CollegeId).OnDelete(DeleteBehavior.Restrict);
            subject.HasIndex(s => new { s.ApplicationId, s.SubjectId }).IsUnique();

            var order = modelBuilder.Entity<PaymentOrder>();
            order.HasOne(o => o.Application).WithMany().HasForeignKey(o => o.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            order.HasOne<College>().WithMany().HasForeignKey(o => o.CollegeId).OnDelete(DeleteBehavior.Restrict);
            order.HasOne<PaymentAccount>().WithMany().HasForeignKey(o => o.PaymentAccountId).OnDelete(DeleteBehavior.Restrict);
            order.HasIndex(o => o.OrderNo).IsUnique();
            order.HasIndex(o => new { o.Status, o.NextCheckAt }); // what the background re-check picks next

            // One account per college; SQL Server's unique index also allows only one NULL, i.e. one platform default.
            var account = modelBuilder.Entity<PaymentAccount>();
            account.HasOne<College>().WithMany().HasForeignKey(a => a.CollegeId).OnDelete(DeleteBehavior.Restrict);
            account.HasIndex(a => a.CollegeId).IsUnique();

            var counter = modelBuilder.Entity<ReceiptCounter>();
            counter.HasKey(c => new { c.CollegeId, c.Series });
            counter.HasOne<College>().WithMany().HasForeignKey(c => c.CollegeId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
