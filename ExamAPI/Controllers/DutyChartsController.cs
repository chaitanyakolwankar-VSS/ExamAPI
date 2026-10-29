using ExamAPI.DTOs;
using ExamAPI.Services.Common;
using ExamAPI.Services.Staff;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>Duty charts: who supervises on which day and session of an exam period, the Excel sheet of the chart, and the supervision letters that follow it. Signed-in staff only (the fallback policy); every query is limited to the user's college.</summary>
    [Route("api/[controller]")]
    [ApiController]
    public class DutyChartsController : ControllerBase
    {
        private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        private readonly IDutyChartService _service;
        private readonly IStaffLetterPdfService _pdf;

        public DutyChartsController(IDutyChartService service, IStaffLetterPdfService pdf)
        {
            _service = service;
            _pdf = pdf;
        }

        /// <summary>The charts of an academic year, newest first.</summary>
        [HttpGet]
        public Task<IActionResult> List([FromQuery] Guid ayid) => Answer(() => _service.ListAsync(ayid));

        /// <summary>A new chart; its days are filled from the timetables.</summary>
        [HttpPost]
        public Task<IActionResult> Create([FromBody] DutyChartSave dto) => Answer(() => _service.CreateAsync(dto));

        /// <summary>The chart with its days, entries and letters.</summary>
        [HttpGet("{id:guid}")]
        public Task<IActionResult> Get(Guid id) => Answer(() => _service.GetAsync(id));

        [HttpPut("{id:guid}")]
        public Task<IActionResult> Update(Guid id, [FromBody] DutyChartSave dto) => Answer(() => _service.UpdateAsync(id, dto));

        /// <summary>Cancels the chart and its letters.</summary>
        [HttpDelete("{id:guid}")]
        public Task<IActionResult> Delete(Guid id) => Answer(async () => (object)new { deleted = true, cancelled = await _service.DeleteAsync(id) });

        /// <summary>Reads the timetables again: adds missing days and fills empty session times. Hand-edited days stay as they are.</summary>
        [HttpPost("{id:guid}/days/refresh")]
        public Task<IActionResult> RefreshDays(Guid id) => Answer(() => _service.RefreshDaysAsync(id));

        /// <summary>Replaces the day list (dates and session times).</summary>
        [HttpPut("{id:guid}/days")]
        public Task<IActionResult> SaveDays(Guid id, [FromBody] List<DutyChartDaySave> days) => Answer(() => _service.SaveDaysAsync(id, days));

        /// <summary>Saves the whole chart: entries, and new, changed and cancelled letters.</summary>
        [HttpPut("{id:guid}/entries")]
        public Task<IActionResult> SaveEntries(Guid id, [FromBody] DutyChartEntriesSave dto) => Answer(() => _service.SaveEntriesAsync(id, dto));

        /// <summary>The chart as a sheet with all active staff: download, fill in, upload.</summary>
        [HttpGet("{id:guid}/excel")]
        public async Task<IActionResult> Excel(Guid id)
        {
            try
            {
                var (bytes, fileName) = await _service.ExcelAsync(id);
                return File(bytes, XlsxType, fileName);
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

        /// <summary>Checks (Preview) or saves the filled-in sheet; the sheet becomes the chart.</summary>
        [HttpPost("{id:guid}/import")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public Task<IActionResult> Import(Guid id, [FromForm] DutyChartImportRequest request) => Answer(() => _service.ImportAsync(id, request));

        /// <summary>Every live supervision letter of the chart in one PDF.</summary>
        [HttpGet("{id:guid}/pdf")]
        public async Task<IActionResult> Pdf(Guid id)
        {
            try
            {
                var (bytes, fileName) = await _pdf.ChartAsync(id);
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
