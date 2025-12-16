using ExamAPI.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] //--comment when want to use without login
    public class AcademicYearController : ControllerBase
    {
        private readonly IAcademicYearService _service;

        public AcademicYearController(IAcademicYearService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var years = await _service.GetAllYearsAsync();
            return Ok(years);
        }
    }
}
