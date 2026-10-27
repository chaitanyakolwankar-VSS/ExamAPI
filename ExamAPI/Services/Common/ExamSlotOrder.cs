using System.Globalization;

namespace ExamAPI.Services.Common
{
    /// <summary>Reads the timetable's date and time text so papers and duty slots can be put in exam order.</summary>
    public static class ExamSlotOrder
    {
        /// <summary>Timetable dates are saved as dd-MM-yyyy text; anything unreadable goes last.</summary>
        public static DateTime Date(string date)
            => DateTime.TryParseExact(date.Trim(), new[] { "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "yyyy-MM-dd" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.MaxValue;

        /// <summary>Times are text like "10:00 AM - 1:00 PM" or "2 PM"; sort on the start time, anything unreadable last.</summary>
        public static TimeSpan StartTime(string time)
        {
            var start = time.Split('-')[0].Trim().ToUpperInvariant().Replace(".", "");
            return DateTime.TryParseExact(start, new[] { "h:mm tt", "hh:mm tt", "h tt", "hh tt", "h:mmtt", "htt", "H:mm", "HH:mm" },
                CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var t) ? t.TimeOfDay : TimeSpan.MaxValue;
        }
    }
}
