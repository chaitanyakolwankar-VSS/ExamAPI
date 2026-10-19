namespace ExamAPI.Services.Auth
{
    /// <summary>Rate-limit policy names (registered in Program.cs, T-14).</summary>
    public static class RateLimits
    {
        /// <summary>Sign-in and OTP verification: 10 requests per minute per IP.</summary>
        public const string SignIn = "sign-in";

        /// <summary>Sending a reset OTP mail: 5 per 15 minutes per IP.</summary>
        public const string SendOtp = "send-otp";

        /// <summary>
        /// Student link pages (open, code, check): 60 per minute per IP. Generous on purpose: a whole class may sign
        /// in from one college network. The real limits are per link (codes per hour, tries per code).
        /// </summary>
        public const string StudentAccess = "student-access";
    }
}
