using ExamAPI.DTOs;
using ExamAPI.Services.RoleMaster;
using ExamAPI.Services.StudentMaster;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StudentMasterController : ControllerBase
    {
        private readonly IStudentMasterService _service;
        public StudentMasterController(IStudentMasterService service)
        {
            _service = service;
        }
        [HttpGet("GetData")]
        public async Task<IActionResult> GetData()
        {
            var role = await _service.GetDataAsync();
            return Ok(role);
        }
        [HttpPost("SaveStudent")]
        public async Task<IActionResult> SaveStudent([FromBody] Savedata dto)
        {
            var studentId = await _service.SaveStudentAsync(dto);

            return Ok(new
            {
                studentId = studentId,
                message = "Student saved successfully"
            });
        }
        [HttpGet("Getbycourse")]
        public async Task<IActionResult> Getbycourse(Guid courseId)
        {
            var data = await _service.GetbycourseAsync(courseId);
            return Ok(data);
        }
        [HttpPost("SearchStudents")]
        public async Task<IActionResult> SearchStudents([FromBody] Searchbyname model)
        {
            var data = await _service.SearchStudentsAsync(model);
            return Ok(data);
        }
    }
}
