using ExamAPI.Models;

namespace ExamAPI.Services.Common
{
    /// <summary>
    /// The one way a student's full name is shown or printed: last, first, middle, mother (University of Mumbai style).
    /// Blank parts are skipped, and the stored capitalisation is kept.
    /// </summary>
    public static class StudentNames
    {
        public static string Display(string? last, string? first, string? middle, string? mother)
        {
            var parts = new[] { last, first, middle, mother }
                .Select(p => p?.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => string.Join(' ', p!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
            return string.Join(' ', parts);
        }

        public static string Display(StudentMaster s) => Display(s.LastName, s.FirstName, s.MiddleName, s.MotherName);
    }
}
