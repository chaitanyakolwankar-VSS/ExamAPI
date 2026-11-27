using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.AtktRevalExam;
using ExamAPI.Services.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.ExamApplications
{
    public interface IExamApplicationAssigner
    {
        /// <summary>
        /// Puts a paid student into the exam with the subjects they applied for, through the same code as the
        /// ATKT/Reval Exam screen. Assigned, or AssignFailed with the reason for staff. Never throws.
        /// </summary>
        Task<bool> AssignAsync(Guid applicationId, CancellationToken ct = default);
    }

    /// <summary>
    /// Runs in a DI scope of its own that acts for the application's college (<see cref="TenantScope"/>): it is called
    /// from the gateway's return and the background re-check, where nobody is signed in.
    /// </summary>
    public sealed class ExamApplicationAssigner : IExamApplicationAssigner
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<ExamApplicationAssigner>? _logger;

        public ExamApplicationAssigner(IServiceScopeFactory scopes, ILogger<ExamApplicationAssigner>? logger = null)
        {
            _scopes = scopes;
            _logger = logger;
        }

        public async Task<bool> AssignAsync(Guid applicationId, CancellationToken ct = default)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var app = await db.ExamApplications.IgnoreQueryFilters()
                .Include(a => a.Subjects)
                .Include(a => a.TargetExam)
                .FirstOrDefaultAsync(a => a.Id == applicationId && !a.IsDeleted, ct);
            if (app?.CollegeId == null || app.TargetExam == null) return false;
            if (app.Status is not (ExamApplicationStatuses.Paid or ExamApplicationStatuses.AssignFailed)) return app.Status == ExamApplicationStatuses.Assigned;

            // From here on this scope reads and writes as the application's college.
            scope.ServiceProvider.GetRequiredService<TenantScope>().CollegeId = app.CollegeId;

            string? error;
            try
            {
                error = await RunAsync(scope.ServiceProvider.GetRequiredService<IAtktRevalExamService>(), app);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Assigning exam application {ApplicationId} failed", applicationId);
                error = "The assignment could not be saved. Try again from Exam Applications.";
            }

            // Re-read: the assignment saved through its own path.
            db.ChangeTracker.Clear();
            app = await db.ExamApplications.IgnoreQueryFilters().FirstAsync(a => a.Id == applicationId, ct);
            if (error == null)
            {
                app.Status = ExamApplicationStatuses.Assigned;
                app.AssignedAt = DateTime.UtcNow;
                app.AssignError = null;
            }
            else
            {
                app.Status = ExamApplicationStatuses.AssignFailed;
                app.AssignError = error.Length > 1000 ? error[..1000] : error;
            }
            app.ConcurrencyStamp = Guid.NewGuid();
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return false; // someone else changed it meanwhile (staff retry); their result stands
            }
            return error == null;
        }

        /// <summary>The assignment, or why it could not be made.</summary>
        private static async Task<string?> RunAsync(IAtktRevalExamService atkt, ExamApplication app)
        {
            var exam = app.TargetExam!;
            if (exam.CourseId == null || exam.AcademicYearAYID == null)
                return "The exam has no course or academic year.";
            if (string.IsNullOrWhiteSpace(app.SemesterId) || string.IsNullOrWhiteSpace(app.Pattern))
                return "The application has no semester or pattern.";
            var subjects = app.Subjects.Select(s => s.SubjectId).ToList();
            if (subjects.Count == 0) return "The application has no subjects.";

            var isReval = app.Kind == ExamApplicationKinds.Revaluation;
            var result = await atkt.SaveAsync(new AtktSaveRequest
            {
                Filter = new AtktMatrixRequest
                {
                    CourseId = exam.CourseId.Value,
                    Ayid = exam.AcademicYearAYID.Value,
                    Semester = app.SemesterId,
                    Pattern = app.Pattern,
                    Mode = isReval ? AssignmentModes.Revaluation : AssignmentModes.Atkt,
                    SourceExamId = isReval ? exam.RevaluationForExamId : null,
                    TargetExamId = exam.ExamId,
                },
                Students = new List<AtktStudentSelectionDto> { new() { StdMstId = app.StdMstId, SubjectIds = subjects } },
            });
            if (!result.Success) return result.Message;
            var data = result.Data;
            if (data == null || data.StudentsAssigned + data.StudentsUpdated == 0)
                return data?.Skipped.Count > 0 ? string.Join(" ", data.Skipped) : "The student was not assigned (not an eligible candidate any more).";
            return null;
        }
    }
}
