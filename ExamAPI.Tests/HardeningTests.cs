using System.Reflection;
using ExamAPI.Controllers;
using ExamAPI.Services.Auth;
using ExamAPI.Services.Common;
using Microsoft.AspNetCore.RateLimiting;

namespace ExamAPI.Tests;

/// <summary>T-13 / T-14: what errors may tell the user, and which endpoints are rate limited.</summary>
public sealed class HardeningTests
{
    [Fact]
    public void Validation_messages_pass_through()
    {
        Assert.Equal("Bad code.", SafeError.Message(new ArgumentException("Bad code.")));
        Assert.Equal("Locked.", SafeError.Message(new InvalidOperationException("Locked.")));
        Assert.Equal("Not found.", SafeError.Message(new KeyNotFoundException("Not found.")));
    }

    [Fact]
    public void Other_errors_are_replaced_by_the_generic_sentence()
    {
        var sqlLike = new Exception("Invalid column name 'HeadFormula'. SELECT [s].[Id] FROM [SubjectCredits]");
        Assert.Equal(SafeError.Generic, SafeError.Message(sqlLike));
        Assert.Equal(SafeError.Generic, SafeError.Message(new NullReferenceException()));
    }

    [Fact]
    public void Sign_in_and_reset_endpoints_are_rate_limited()
    {
        static string? Policy(MemberInfo m) => m.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;

        Assert.Equal(RateLimits.SignIn, Policy(typeof(AuthController)));
        Assert.Equal(RateLimits.SignIn, Policy(typeof(SendResetOtpController)));
        Assert.Equal(RateLimits.SendOtp, Policy(typeof(SendResetOtpController).GetMethod(nameof(SendResetOtpController.SendResetOtp))!));
    }
}
