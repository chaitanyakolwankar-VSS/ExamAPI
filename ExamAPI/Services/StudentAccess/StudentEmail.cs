using System.Net.Mail;
using System.Text.RegularExpressions;
using ExamAPI.Data;
using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.StudentAccess
{
    /// <summary>
    /// Student email rules, shared by the Excel import, the email sheet, the single-student form and the review
    /// screen. Emails are stored trimmed and lower-case and are unique per college. A wrong format or an email
    /// that already belongs to another student is an error (nothing is saved); a domain outside the college's
    /// allowed list is saved but Flagged, so staff can check it before links are sent.
    /// </summary>
    public static partial class StudentEmail
    {
        public const int MaxLength = 255;

        /// <summary>Trimmed and lower-case; "" when blank.</summary>
        public static string Normalize(string? value) => (value ?? "").Trim().ToLowerInvariant();

        // One @, no spaces, a dot in the domain: what MailAddress alone lets through ("a@b", "a b@c.d") is refused.
        [GeneratedRegex(@"^[a-z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)+$")]
        private static partial Regex Shape();

        /// <summary>Whether a normalized email has a sendable shape.</summary>
        public static bool IsValid(string email) =>
            email.Length <= MaxLength
            && Shape().IsMatch(email)
            && !email.Contains("..")
            && MailAddress.TryCreate(email, out var parsed)
            && parsed.Address == email;

        /// <summary>The allowed domains from the college settings ("a.in, b.in"), lower-case; empty = any domain.</summary>
        public static string[] ParseDomains(string? allowed) =>
            (allowed ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => d.TrimStart('@').ToLowerInvariant())
                .Where(d => d != "")
                .Distinct()
                .ToArray();

        /// <summary>Why a valid email is flagged, or null when it is fine.</summary>
        public static string? FlagReason(string email, string[] allowedDomains)
        {
            if (allowedDomains.Length == 0) return null;
            var domain = email[(email.IndexOf('@') + 1)..];
            return allowedDomains.Contains(domain) ? null : $"Domain {domain} is not one of the college's ({string.Join(", ", allowedDomains)}).";
        }

        /// <summary>The allowed domains of the caller's college.</summary>
        public static async Task<string[]> AllowedDomainsAsync(ApplicationDbContext context) =>
            ParseDomains(await context.CollegeSettings.Select(s => s.AllowedEmailDomains).FirstOrDefaultAsync());

        /// <summary>Which of these emails already belong to a student of the caller's college: email -> that student's ID.</summary>
        public static async Task<Dictionary<string, string>> OwnersAsync(ApplicationDbContext context, IEnumerable<string> emails)
        {
            var list = emails.Where(e => e != "").Distinct().ToList();
            if (list.Count == 0) return new Dictionary<string, string>();
            var owners = await context.StudentMasters
                .Where(s => s.Email != null && list.Contains(s.Email))
                .Select(s => new { s.Email, s.StudentId })
                .ToListAsync();
            return owners.GroupBy(o => o.Email!).ToDictionary(g => g.Key, g => g.First().StudentId);
        }

        /// <summary>Sets a student's email with its status: Flagged when outside the allowed domains, else not-yet-checked.</summary>
        public static void Apply(StudentMaster student, string email, string[] allowedDomains)
        {
            if (email == "")
            {
                student.Email = null;
                student.EmailStatus = null;
                student.EmailFlagReason = null;
                return;
            }
            var flag = FlagReason(email, allowedDomains);
            student.Email = email;
            student.EmailStatus = flag == null ? null : EmailVerification.Flagged;
            student.EmailFlagReason = flag;
        }
    }
}
