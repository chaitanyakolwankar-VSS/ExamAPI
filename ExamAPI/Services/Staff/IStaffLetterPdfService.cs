namespace ExamAPI.Services.Staff
{
    /// <summary>
    /// Staff letters as PDF (server-made, like marksheets). Reads only; every lookup goes through the tenant filter, so
    /// a letter of another college is "not found". Cancelled letters are not printed.
    /// </summary>
    public interface IStaffLetterPdfService
    {
        /// <summary>One letter. Throws KeyNotFoundException when it does not exist or was cancelled.</summary>
        Task<(byte[] Bytes, string FileName)> OneAsync(Guid letterId);

        /// <summary>
        /// Every live letter of an exam (any branch's copy) in one PDF, one letter per page, in letter-number order.
        /// <paramref name="kind"/>: "supervision" or "appointment" (examiner and moderator letters).
        /// </summary>
        Task<(byte[] Bytes, string FileName)> ManyAsync(Guid examId, string kind);
    }
}
