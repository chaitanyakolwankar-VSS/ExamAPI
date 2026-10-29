using ExamAPI.DTOs;
using ExamAPI.Services.Auth;
using ExamAPI.Services.Common;
using ExamAPI.Services.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>Letters to staff (examiner and moderator appointments; supervision letters come from the duty charts). Signed-in staff only (the fallback policy); every query is limited to the user's college. The letter settings can be changed by a college admin only.</summary>
    [Route("api/[controller]")]
    [ApiController]
    public class StaffLettersController : ControllerBase
    {
        private readonly IStaffLetterService _service;
        private readonly IStaffLetterPdfService _pdf;

        public StaffLettersController(IStaffLetterService service, IStaffLetterPdfService pdf)
        {
            _service = service;
            _pdf = pdf;
        }

        /// <summary>The exams of an academic year that have a timetable, one per exam (all branches together), for the examiner letters.</summary>
        [HttpGet("exams")]
        public Task<IActionResult> Exams([FromQuery] Guid ayid) => Answer(() => _service.ExamsAsync(ayid));

        /// <summary>One exam's subjects and its examiner and moderator letters. Any branch's copy of the exam will do.</summary>
        [HttpGet("appointments")]
        public Task<IActionResult> Appointments([FromQuery] Guid examId) => Answer(() => _service.AppointmentsAsync(examId));

        /// <summary>Saves the whole Examiner Letters screen of an exam: new letters, cancelled letters.</summary>
        [HttpPut("appointments")]
        public Task<IActionResult> SaveAppointments([FromBody] AppointmentSave dto) => Answer(() => _service.SaveAppointmentsAsync(dto));

        [HttpGet("settings")]
        public Task<IActionResult> Settings() => Answer(() => _service.GetSettingsAsync());

        [HttpPut("settings")]
        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        public Task<IActionResult> SaveSettings([FromBody] LetterSettingsSave dto) => Answer(() => _service.SaveSettingsAsync(dto));

        /// <summary>One letter as a PDF. A cancelled letter or one of another college is not found.</summary>
        [HttpGet("{letterId:guid}/pdf")]
        public Task<IActionResult> LetterPdf(Guid letterId) => AnswerPdf(() => _pdf.OneAsync(letterId));

        /// <summary>Every letter of an exam in one PDF, one per page. kind: supervision or appointment (examiner and moderator letters).</summary>
        [HttpGet("pdf")]
        public Task<IActionResult> LettersPdf([FromQuery] Guid examId, [FromQuery] string? kind) => AnswerPdf(() => _pdf.ManyAsync(examId, kind ?? ""));

        private async Task<IActionResult> AnswerPdf(Func<Task<(byte[] Bytes, string FileName)>> action)
        {
            try
            {
                var (bytes, fileName) = await action();
                return File(bytes, "application/pdf", fileName);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = SafeError.Message(ex) });
            }
        }

        private async Task<IActionResult> Answer<T>(Func<Task<T>> action)
        {
            try
            {
                return Ok(await action());
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = SafeError.Message(ex) });
            }
        }
    }
}
