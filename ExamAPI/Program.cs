using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Auth;
using ExamAPI.Services.Dashboard;
using ExamAPI.Services.Email;
using ExamAPI.Services.PasswordResetOTP;
using ExamAPI.Services.Result.Engine;
using ExamAPI.Services.RoleMaster;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using OfficeOpenXml;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

// Tenant identity, read from the JWT. ApplicationDbContext depends on this to build its
// global college query filter, so it must be registered before the DbContext.
builder.Services.AddScoped<ExamAPI.Services.Tenancy.ICurrentUser, ExamAPI.Services.Tenancy.CurrentUser>();
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

//  connection string
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

//  connection string end ------------//


//--services and interface ------//
// Uploaded student photos/signatures and college logos: persistent, non-public storage (DB-07 / DEC-12).
// Root = Storage:UploadsRoot (absolute path recommended, outside the publish folder); default <ContentRoot>/App_Data/uploads.
builder.Services.AddSingleton<ExamAPI.Services.Files.IFileStorage>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var config = sp.GetRequiredService<IConfiguration>();
    return new ExamAPI.Services.Files.FileStorage(env.ContentRootPath, env.WebRootPath, config["Storage:UploadsRoot"]);
});
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ExamAPI.Services.Auth.IAuthService, ExamAPI.Services.Auth.AuthService>();
builder.Services.AddScoped<ExamAPI.Services.Common.IGenericRepository, ExamAPI.Services.Common.GenericRepository>();
builder.Services.AddScoped<ExamAPI.Services.Common.IAcademicYearService, ExamAPI.Services.Common.AcademicYearService>();
builder.Services.AddScoped<ExamAPI.Services.Ordinance.IOrdinanceService, ExamAPI.Services.Ordinance.OrdinanceService>(); 
builder.Services.AddScoped<ExamAPI.Services.Permissions.IPermissionService, ExamAPI.Services.Permissions.PermissionService>();
builder.Services.AddScoped<ExamAPI.Services.CollegeDetail.ICollegeDetailService, ExamAPI.Services.CollegeDetail.CollegeDetailService>();
builder.Services.AddScoped<IRoleMasterService, RoleMasterService>();
builder.Services.AddScoped<ExamAPI.Services.Subject.ISubjectService, ExamAPI.Services.Subject.SubjectService>();
builder.Services.AddScoped<ExamAPI.Services.StudentMasters.IStudentMasterService, ExamAPI.Services.StudentMasters.StudentMasterService>();
builder.Services.AddScoped<ExamAPI.Services.Exam.IExamService, ExamAPI.Services.Exam.ExamService>();
builder.Services.AddScoped<ExamAPI.Services.RegularExam.IRegularExamService, ExamAPI.Services.RegularExam.RegularExamService>();
builder.Services.AddScoped<ExamAPI.Services.Eligibility.IEligibilityService,ExamAPI.Services.Eligibility.EligibilityService>();
builder.Services.AddScoped<ExamAPI.Services.GenerateHallTicket.IGenerateHallTicketService, ExamAPI.Services.GenerateHallTicket.GenerateHallTicketService>();
builder.Services.AddScoped<ExamAPI.Services.UsersMaster.IUserMasterService, ExamAPI.Services.UsersMaster.UserMasterService>();
builder.Services.AddScoped<ExamAPI.Services.Platform.IProvisionCollegeService, ExamAPI.Services.Platform.ProvisionCollegeService>();
builder.Services.AddScoped<ExamAPI.Services.Platform.IPlatformCollegeService, ExamAPI.Services.Platform.PlatformCollegeService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<ExamAPI.Services.DeclareResult.IDResultService, ExamAPI.Services.DeclareResult.DResultService>();
builder.Services.AddScoped<ExamAPI.Services.AssignSeatNo.IAssignSeatNoService, ExamAPI.Services.AssignSeatNo.AssignSeatNoService>();
builder.Services.AddScoped<ExamAPI.Services.AtktRevalExam.IAtktRevalExamService, ExamAPI.Services.AtktRevalExam.AtktRevalExamService>();
builder.Services.AddScoped<ExamAPI.Services.Result.IResultService, ExamAPI.Services.Result.ResultService>();
builder.Services.AddScoped<ExamAPI.Services.MarksEntry.IMarksEntryService, ExamAPI.Services.MarksEntry.MarksEntryService>();
builder.Services.AddScoped<ExamAPI.Services.Report.IReportService, ExamAPI.Services.Report.ReportService>();
builder.Services.AddScoped<ExamAPI.Services.StatisticalReport.IStatisticalReportService, ExamAPI.Services.StatisticalReport.StatisticalReportService>();
builder.Services.AddScoped<ExamAPI.Services.ATKTCummulativeReport.IATKTCummulativeReportService, ExamAPI.Services.ATKTCummulativeReport.ATKTCummulativeReportService>();
builder.Services.AddScoped<ExamAPI.Services.StudentPromotion.IStudentPromotionService, ExamAPI.Services.StudentPromotion.StudentPromotion>();
builder.Services.AddScoped<ExamAPI.Services.ReleaseHallTicket.IReleaseHallticketService, ExamAPI.Services.ReleaseHallTicket.ReleaseHallticketService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ExamAPI.Services.Lookup.ILookupService, ExamAPI.Services.Lookup.LookupService>();
builder.Services.AddOrdinanceEngine();


QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

//--services and interface end ------//



// JWT Authentication
// A missing or short signing key must stop the start, not produce tokens anyone could forge.
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Jwt:Key must be set to a random value of at least 32 characters.");
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not set.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});
// JWT Authentication end


// Authorization: authentication is OPT-OUT, not opt-in.
// Every endpoint now requires a valid token unless it is explicitly marked
// [AllowAnonymous] (currently only AuthController and SendResetOtpController).
// Previously only 5 of 19 controllers carried [Authorize], which left the rest --
// including StudentMaster and UserMaster -- readable and writable with no token at all.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // Access model (DEC-17 / DB-05): PlatformAdmin and CollegeAdmin policies.
    options.AddAccessPolicies();
});
// Authorization end


//CORS config
// Only needed when the site and the API are on different origins (e.g. the Vite dev server). On IIS both
// are under one host, so production lists nothing or just that host. Cors:AllowedOrigins in appsettings.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? (builder.Environment.IsDevelopment() ? new[] { "http://localhost:5173", "http://localhost:5174" } : Array.Empty<string>());
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy => policy
            .WithOrigins(corsOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader());

});
//CORS config

// Unhandled exceptions: logged, and answered with a ProblemDetails body without internals (T-13).
builder.Services.AddProblemDetails();

// Rate limits per client IP (T-14): sign-in and OTP checks, and a tighter one for sending reset mails.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(ExamAPI.Services.Auth.RateLimits.SignIn, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy(ExamAPI.Services.Auth.RateLimits.SendOtp, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
    options.OnRejected = async (context, ct) =>
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many attempts. Please wait a few minutes and try again." }, ct);
});


//---mainbuild
var app = builder.Build();
//---mainbuild end

ExamAPI.Services.Common.SafeError.Logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Errors");

// First start on an empty database: platform admin (from Bootstrap:* settings) and the screen catalog.
// Only ever adds; a failure is logged and does not stop the app.
using (var scope = app.Services.CreateScope())
{
    var seedLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StartupSeeder");
    try
    {
        await ExamAPI.Services.Platform.StartupSeeder.SeedAsync(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), app.Configuration, seedLogger);
    }
    catch (Exception ex)
    {
        seedLogger.LogError(ex, "Startup: seeding failed.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler();
}

// Basic response hardening; the Content-Security-Policy comes with T-18.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});
// No app.UseStaticFiles(): wwwroot held only uploaded personal data (student photos/signatures, college
// logos), which must not be anonymously readable (DEC-12). They are served solely by the authorised
// GET /api/Files endpoint. Re-add static files only with an explicit block for /uploads and /Clg_detail*.
app.UseHttpsRedirection();

app.UseCors("AllowReactApp");
app.UseRateLimiter();
// Authentication & Authorization(ORDER MATTERS: Authentication (Who are you?) -> Authorization (Are you allowed?))
app.UseAuthentication();
app.UseAuthorization();
// Authentication & Authorization end

app.MapControllers();

app.Run();


