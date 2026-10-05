namespace ExamAPI.Services.Common
{
    /// <summary>
    /// The text a caught exception may show to the user (T-13). Validation and business-rule failures
    /// (ArgumentException, InvalidOperationException, KeyNotFoundException) carry messages written for
    /// the user and pass through. Anything else (database errors, null references, ...) can expose
    /// SQL, table names or stack details, so it is logged and replaced by a generic sentence.
    /// </summary>
    public static class SafeError
    {
        public const string Generic = "Something went wrong. The error has been logged; please try again or contact support.";

        /// <summary>Set once at startup (Program.cs).</summary>
        public static ILogger? Logger { get; set; }

        public static bool IsUserFacing(Exception ex) =>
            ex is ArgumentException or InvalidOperationException or KeyNotFoundException;

        public static string Message(Exception ex)
        {
            if (IsUserFacing(ex)) return ex.Message;
            Logger?.LogError(ex, "Handled error");
            return Generic;
        }
    }
}
