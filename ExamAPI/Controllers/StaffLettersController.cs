using ExamAPI.DTOs;
using ExamAPI.Services.Auth;
using ExamAPI.Services.Common;
using ExamAPI.Services.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>Letters to staff (supervision duties, examiner and moderator appointments). Signed-in staff only (the fallback policy); every query is limited to the user's college. The letter settings can be changed by a college admin only.</summary>
    [Route("api/[controller]")]
    [ApiController]
    public class StaffLettersController : ControllerBase
    {
        private readonly IStaffLetterService _service;

        public StaffLettersController(IStaffLetterService service)
        {
            _service = service;
        }

        /// <summary>The exams of an academic year that have a timetable, one per exam (all branches together).</summary>
        [HttpGet("exams")]
        public Task<IActionResult> Exams([FromQuery] Guid ayid) => Answer(() => _service.ExamsAsync(ayid));

        /// <summary>One exam's duty slots and its letters. Any branch's copy of the exam will do.</summary>
        [HttpGet("supervision")]
        public Task<IActionResult> Supervision([FromQuery] Guid examId) => Answer(() => _service.SupervisionAsync(examId));

        /// <summary>Saves the whole assignment grid of an exam: new letters, changed letters, cancelled letters.</summary>
        [HttpPut("supervision")]
        public Task<IActionResult> SaveSupervision([FromBody] SupervisionSave dto) => Answer(() => _service.SaveSupervisionAsync(dto));

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
