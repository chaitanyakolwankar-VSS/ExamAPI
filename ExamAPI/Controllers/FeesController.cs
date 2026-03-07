using ExamAPI.DTOs;
using ExamAPI.Services.Fees;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FeesController : ControllerBase
    {
        private readonly IFeesService _feesService;

        public FeesController(IFeesService feesService)
        {
            _feesService = feesService;
        }

        [HttpGet("get-branch")]
        public async Task<IActionResult> GetCourses()
        {
            var res = await _feesService.GetCoursesAsync();
            return Ok(res);
        }

        [HttpGet("get-exams")]
        public async Task<IActionResult> GetExams(
    [FromQuery] Guid ayid,
    [FromQuery] Guid? courseId)
        {
            var res = await _feesService.GetExamsAsync(ayid,  courseId );
            return Ok(res);
        }
        [HttpGet("get-fees")]
        public async Task<IActionResult> GetFees([FromQuery] GetFeesDto dto)
        {
            var res = await _feesService.GetFeesByExamAsync(
                dto
                );
            return Ok(res);
        }

        [HttpGet("get-categories")]
        public async Task<IActionResult> GetCategories()
        {
            var res = await _feesService.GetCategoriesAsync();
            return Ok(res);
        }

        [HttpPost("save-fees")]
        public async Task<IActionResult> SaveFees([FromBody] SaveFees dto)
        {
            var res = await _feesService.SaveFeesAsync(dto);
            return Ok(res);
        }

        [HttpDelete("delete-fees")]
        public async Task<IActionResult> DeleteFees([FromQuery] DeleteFeesDto dto)
        {
         var res = await _feesService.DeleteFeesAsync(dto);
            return Ok(res);
        }
    }
}
