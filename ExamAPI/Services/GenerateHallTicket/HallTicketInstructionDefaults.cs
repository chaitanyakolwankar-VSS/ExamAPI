using ExamAPI.Models;

namespace ExamAPI.Services.GenerateHallTicket
{
    /// <summary>The "Instructions to candidates" printed on hall tickets when the college has not written its own.</summary>
    public static class HallTicketInstructionDefaults
    {
        public const int MaxLines = 10;
        public const int MaxLineLength = 250;
        public const int MaxLength = 1500;

        public static readonly IReadOnlyList<string> Lines = new[]
        {
            "Report to the examination hall at least 30 minutes before the paper starts.",
            "Check your name, photo and signature on this hall ticket. They are used on the mark sheet and convocation certificate, so report any mistake to the exam office at once.",
            "Permitted: pens, pencils, eraser, transparent water bottle, clear pouch, and a non-programmable calculator only where the syllabus allows it.",
            "Not permitted: mobile phones, smart watches, Bluetooth or other electronic devices, books, notes, opaque pencil cases and bags.",
            "Carrying notes or electronic items, even switched off or unused, is treated as Unfair Means (UMP).",
            "See the notice board for any updates."
        };

        /// <summary>The lines of a stored text: split on new lines, trimmed, blank lines dropped.</summary>
        public static List<string> Split(string? text)
            => (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l != "").ToList();

        /// <summary>What the hall ticket prints: nothing when the college switched them off, its own lines when set, else the defaults.</summary>
        public static List<string> Effective(CollegeSettings? s)
        {
            if (s?.HallTicketInstructionsOff == true) return new List<string>();
            var own = Split(s?.HallTicketInstructions);
            return own.Count > 0 ? own : Lines.ToList();
        }
    }
}
