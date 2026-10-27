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
            letter.HasIndex(l => new { l.StaffId, l.ExamId, l.LetterType, l.SubjectId }).IsUnique().HasFilter("[IsDeleted] = 0");
            letter.HasOne<College>().WithMany().HasForeignKey(l => l.CollegeId).OnDelete(DeleteBehavior.Restrict);
            letter.HasOne(l => l.Staff).WithMany().HasForeignKey(l => l.StaffId).OnDelete(DeleteBehavior.Restrict);
            letter.HasOne<ExamMaster>().WithMany().HasForeignKey(l => l.ExamId).OnDelete(DeleteBehavior.Restrict);
            letter.Property(l => l.LetterDate).HasColumnType("date");

            var duty = modelBuilder.Entity<StaffLetterDuty>();
            duty.HasOne<College>().WithMany().HasForeignKey(d => d.CollegeId).OnDelete(DeleteBehavior.Restrict);
            duty.HasOne(d => d.Letter).WithMany(l => l.Duties).HasForeignKey(d => d.LetterId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
