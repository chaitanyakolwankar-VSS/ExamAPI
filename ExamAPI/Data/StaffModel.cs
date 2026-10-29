using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Data
{
    /// <summary>Keys, indexes and relations of the staff tables (see Models/Staff.cs).</summary>
    public static class StaffModel
    {
        public static void Configure(ModelBuilder modelBuilder)
        {
            var staff = modelBuilder.Entity<StaffMaster>();
            // One person per employee ID and per email in a college (live rows only).
            staff.HasIndex(s => new { s.CollegeId, s.EmployeeCode }).IsUnique()
                .HasFilter("[EmployeeCode] IS NOT NULL AND [IsDeleted] = 0");
            staff.HasIndex(s => new { s.CollegeId, s.Email }).IsUnique().HasFilter("[IsDeleted] = 0");
            // NO ACTION everywhere: nothing is hard-deleted, and a list entry in use cannot be removed.
            staff.HasOne<College>().WithMany().HasForeignKey(s => s.CollegeId).OnDelete(DeleteBehavior.Restrict);
            staff.HasOne(s => s.Department).WithMany().HasForeignKey(s => s.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            staff.HasOne(s => s.Designation).WithMany().HasForeignKey(s => s.DesignationId).OnDelete(DeleteBehavior.Restrict);

            var option = modelBuilder.Entity<StaffOption>();
            option.HasIndex(o => new { o.CollegeId, o.Kind, o.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
            option.HasOne<College>().WithMany().HasForeignKey(o => o.CollegeId).OnDelete(DeleteBehavior.Restrict);

            // Letters: a number is unique per college, type and year, and a person gets one letter per exam (and subject) and type.
            // Both are checked on live rows only: a cancelled letter keeps its number but lets the person be assigned again.
            var letter = modelBuilder.Entity<StaffLetter>();
            letter.HasIndex(l => new { l.CollegeId, l.LetterType, l.AYID, l.SerialNo }).IsUnique().HasFilter("[IsDeleted] = 0");
            letter.HasIndex(l => new { l.StaffId, l.ExamId, l.DutyChartId, l.LetterType, l.SubjectId }).IsUnique().HasFilter("[IsDeleted] = 0");
            letter.HasOne<College>().WithMany().HasForeignKey(l => l.CollegeId).OnDelete(DeleteBehavior.Restrict);
            letter.HasOne(l => l.Staff).WithMany().HasForeignKey(l => l.StaffId).OnDelete(DeleteBehavior.Restrict);
            letter.HasOne<ExamMaster>().WithMany().HasForeignKey(l => l.ExamId).OnDelete(DeleteBehavior.Restrict);
            letter.HasOne<DutyChart>().WithMany().HasForeignKey(l => l.DutyChartId).OnDelete(DeleteBehavior.Restrict);
            letter.Property(l => l.LetterDate).HasColumnType("date");

            var duty = modelBuilder.Entity<StaffLetterDuty>();
            duty.HasOne<College>().WithMany().HasForeignKey(d => d.CollegeId).OnDelete(DeleteBehavior.Restrict);
            duty.HasOne(d => d.Letter).WithMany(l => l.Duties).HasForeignKey(d => d.LetterId).OnDelete(DeleteBehavior.Restrict);
            duty.Property(d => d.Kind).HasDefaultValue(DutyKinds.Duty);

            // Existing staff are teaching staff until marked otherwise.
            staff.Property(s => s.IsTeaching).HasDefaultValue(true);

            // Duty charts: one row per day, one entry per person per day.
            var chart = modelBuilder.Entity<DutyChart>();
            chart.HasOne<College>().WithMany().HasForeignKey(c => c.CollegeId).OnDelete(DeleteBehavior.Restrict);
            chart.Property(c => c.FromDate).HasColumnType("date");
            chart.Property(c => c.ToDate).HasColumnType("date");

            var day = modelBuilder.Entity<DutyChartDay>();
            day.HasOne(d => d.Chart).WithMany(c => c.Days).HasForeignKey(d => d.ChartId).OnDelete(DeleteBehavior.Restrict);
            day.HasOne<College>().WithMany().HasForeignKey(d => d.CollegeId).OnDelete(DeleteBehavior.Restrict);
            day.HasIndex(d => new { d.ChartId, d.Date }).IsUnique();
            day.Property(d => d.Date).HasColumnType("date");

            var entry = modelBuilder.Entity<DutyChartEntry>();
            entry.HasOne(e => e.Chart).WithMany(c => c.Entries).HasForeignKey(e => e.ChartId).OnDelete(DeleteBehavior.Restrict);
            entry.HasOne(e => e.Staff).WithMany().HasForeignKey(e => e.StaffId).OnDelete(DeleteBehavior.Restrict);
            entry.HasOne<College>().WithMany().HasForeignKey(e => e.CollegeId).OnDelete(DeleteBehavior.Restrict);
            entry.HasIndex(e => new { e.ChartId, e.StaffId, e.Date }).IsUnique();
            entry.Property(e => e.Date).HasColumnType("date");

            var member = modelBuilder.Entity<DutyChartMember>();
            member.HasOne(m => m.Chart).WithMany().HasForeignKey(m => m.ChartId).OnDelete(DeleteBehavior.Restrict);
            member.HasOne(m => m.Staff).WithMany().HasForeignKey(m => m.StaffId).OnDelete(DeleteBehavior.Restrict);
            member.HasOne<College>().WithMany().HasForeignKey(m => m.CollegeId).OnDelete(DeleteBehavior.Restrict);
            member.HasIndex(m => new { m.ChartId, m.StaffId }).IsUnique();
        }
    }
}
