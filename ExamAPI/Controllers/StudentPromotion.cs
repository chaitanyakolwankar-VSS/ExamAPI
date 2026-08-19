using ExamAPI.DTOs;
using ExamAPI.Services.StudentPromotion;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StudentPromotion : Controller
    {
        public readonly IStudentPromotionService _StudentPromotionService;

        public StudentPromotion(IStudentPromotionService studentpromotion)
        {
            _StudentPromotionService = studentpromotion;
        }

        // GET Students Data
        [HttpGet("GetStudentData")]
        public async Task<IActionResult> GetStudentData([FromQuery] SingleStudentDataRequest dto)
        {
            var result = await _StudentPromotionService.GetStudentData(dto);
            return Ok(result);
        }
        // GET Assigned Students
        [HttpGet("GetAssignedStudent")]
        public async Task<IActionResult> GetAssignedStudent([FromQuery] EligibilityStudentsAssign dto)
        {
            var result = await _StudentPromotionService.GetAssignedStudent(dto);
            return Ok(result);
        }

        //Save Eligibility
        [HttpPost("SaveEligibility")]
        public async Task<IActionResult> SaveEligibility([FromBody] SaveEligibility dto)
        {
            var result = await _StudentPromotionService.SaveEligibility(dto);
            return Ok(result);
        }
        [HttpPut("UpdateEligibility")]
        public async Task<IActionResult> UpdateEligibility(
    [FromBody] UpdateEligibility request)
        {
            var result = await _StudentPromotionService.UpdateEligibility(request);

            return Ok(result);
        }
    }
}
