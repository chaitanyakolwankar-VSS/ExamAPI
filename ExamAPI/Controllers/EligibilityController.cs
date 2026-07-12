using ExamAPI.DTOs;
using ExamAPI.Services.Eligibility;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EligibilityController : Controller
    {
        public readonly IEligibilityService _EligibilityService;
        public EligibilityController(IEligibilityService eligibility)
        {
            _EligibilityService = eligibility;
        }
        [HttpGet("get-Students")]
        public async Task<IActionResult> GetStudents([FromQuery] GetEligibilityStudents Request)
        {
            var Results =await _EligibilityService.EligibilityStudents(Request);
            return Ok(Results);
        }
        [HttpPost("save-Eligibility")]
        public async Task<IActionResult> SaveEligibility([FromBody] List<EligibilityStudents> Request)
        {
            var Results = await _EligibilityService.SaveEligibility(Request);
            return Ok(Results);
        }
    }
}
