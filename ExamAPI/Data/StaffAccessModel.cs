using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Data
{
    /// <summary>Keys, indexes and relations of the staff-link tables (see Models/StaffAccess.cs). NO ACTION everywhere, like the student ones.</summary>
    public static class StaffAccessModel
    {
        public static void Configure(ModelBuilder modelBuilder)
        {
            var link = modelBuilder.Entity<StaffLink>();
            link.HasOne(l => l.Staff).WithMany().HasForeignKey(l => l.StaffId).OnDelete(DeleteBehavior.Restrict);
            link.HasOne<College>().WithMany().HasForeignKey(l => l.CollegeId).OnDelete(DeleteBehavior.Restrict);
            link.HasOne<ExamMaster>().WithMany().HasForeignKey(l => l.ExamId).OnDelete(DeleteBehavior.Restrict);
            link.HasIndex(l => l.TokenHash).IsUnique().HasFilter("[TokenHash] IS NOT NULL");
            link.HasIndex(l => new { l.StaffId, l.ExamId, l.Status });

            var otp = modelBuilder.Entity<StaffOtp>();
            otp.HasOne(o => o.Link).WithMany().HasForeignKey(o => o.LinkId).OnDelete(DeleteBehavior.Restrict);
            otp.HasOne<College>().WithMany().HasForeignKey(o => o.CollegeId).OnDelete(DeleteBehavior.Restrict);
            otp.HasIndex(o => new { o.LinkId, o.CreatedAt }); // "3 codes per hour per link"

            var session = modelBuilder.Entity<StaffSession>();
            session.HasOne(s => s.Link).WithMany().HasForeignKey(s => s.LinkId).OnDelete(DeleteBehavior.Restrict);
            session.HasOne<StaffMaster>().WithMany().HasForeignKey(s => s.StaffId).OnDelete(DeleteBehavior.Restrict);
            session.HasOne<College>().WithMany().HasForeignKey(s => s.CollegeId).OnDelete(DeleteBehavior.Restrict);
            session.HasIndex(s => s.SessionHash).IsUnique();
        }
    }
}
