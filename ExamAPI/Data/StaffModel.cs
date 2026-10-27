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
        }
    }
}
