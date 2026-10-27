using ExamAPI.DTOs;
using ExamAPI.Services.Common;
using ExamAPI.Services.Staff;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>Staff Master: the people who get exam letters, the department / designation lists and the Excel sheet. Signed-in staff only (the fallback policy); every query is limited to the user's college.</summary>
    [Route("api/[controller]")]
    [ApiController]
    public class StaffController : ControllerBase
    {
        private readonly IStaffService _service;

        public StaffController(IStaffService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] StaffFilter filter) => Ok(await _service.ListAsync(filter));

        [HttpGet("{staffId:guid}")]
        public Task<IActionResult> Get(Guid staffId) => Answer(() => _service.GetAsync(staffId));

        [HttpPost]
        public Task<IActionResult> Add([FromBody] StaffSave dto) => Answer(() => _service.SaveAsync(null, dto));

        [HttpPut("{staffId:guid}")]
        public Task<IActionResult> Update(Guid staffId, [FromBody] StaffSave dto) => Answer(() => _service.SaveAsync(staffId, dto));

        [HttpDelete("{staffId:guid}")]
        public Task<IActionResult> Delete(Guid staffId) => Answer(async () => { await _service.DeleteAsync(staffId); return (object)new { deleted = true }; });

        [HttpGet("options")]
        public async Task<IActionResult> Options() => Ok(await _service.OptionsAsync());

        [HttpPost("options")]
        public Task<IActionResult> AddOption([FromBody] StaffOptionSave dto) => Answer(() => _service.AddOptionAsync(dto));

        [HttpPut("options/{id:guid}")]
        public Task<IActionResult> RenameOption(Guid id, [FromBody] StaffOptionSave dto) => Answer(() => _service.RenameOptionAsync(id, dto));

        [HttpDelete("options/{id:guid}")]
        public Task<IActionResult> DeleteOption(Guid id) => Answer(async () => { await _service.DeleteOptionAsync(id); return (object)new { deleted = true }; });

        /// <summary>The staff sheet with the current staff filled in (edit and import it back).</summary>
        [HttpGet("template")]
        public async Task<IActionResult> Template()
        {
            var (bytes, name) = await _service.TemplateAsync();
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name + ".xlsx");
        }

        [HttpPost("import")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public Task<IActionResult> Import([FromForm] StaffImportRequest request) => Answer(() => _service.ImportAsync(request));

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
