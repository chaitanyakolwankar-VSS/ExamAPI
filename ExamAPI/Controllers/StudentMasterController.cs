
using DocumentFormat.OpenXml.Spreadsheet;
using ExamAPI.DTOs;

using ExamAPI.Services.Auth;
using ExamAPI.Services.RoleMaster;
using ExamAPI.Services.StudentMasters;
using Microsoft.AspNetCore.Authorization;
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
        // Course list only (ayid is ignored); kept for team branches, remove after they migrate (T-19 D).
        [Obsolete("Use /api/Lookup/bootstrap (courses) -- kept for team branches; remove after they migrate (T-19 D)")]
        [HttpGet("GetData")]
        public async Task<IActionResult> GetData(Guid ayid)
        {
            var role = await _service.GetDataAsync(ayid);
            return Ok(role);
        }

        [HttpGet("Getbycourse")]
        public async Task<IActionResult> Getbycourse(Guid courseId, Guid ayid)
        {
            var data = await _service.GetbycourseAsync(courseId, ayid);
            return Ok(data);
        }
        [HttpPost("SearchStudents")]
        public async Task<IActionResult> SearchStudents([FromBody] Searchbyname model)
        {
            var data = await _service.SearchStudentsAsync(model, model.AYID);
            return Ok(data);
        }

        [HttpGet("GetStudentById")]
        public async Task<IActionResult> GetStudentById(string studentId, Guid ayid)
        {
            var data = await _service.GetStudentByIdAsync(studentId, ayid);
            return Ok(data);
        }
        [HttpPost("SaveStudent")]
        public async Task<IActionResult> SaveStudent([FromBody] Savedata dto)
        {
            try
            {
                var studentId = await _service.SaveStudentAsync(dto);

                return Ok(studentId);
            }
            catch(Exception ex)
            {
              return BadRequest(ex.Message);
            }
        }
        [HttpPut("UpdateStudent")]
        public async Task<IActionResult> UpdateStudent([FromBody] Savedata dto)
        {
            try
            {
                var studentId = await _service.UpdateStudentAsync(dto);

                return Ok(studentId);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
           
        }

        /// <summary>Uploads a student's photo and/or signature (data: URLs); other fields are untouched.</summary>
        [HttpPut("UpdateImages")]
        public async Task<IActionResult> UpdateImages([FromBody] StudentImagesDto dto)
        {
            try
            {
                var (photoUrl, signUrl) = await _service.UpdateImagesAsync(dto);
                return Ok(new { photoUrl, signUrl });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("DeleteStudent")]
        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        public async Task<IActionResult> DeleteStudent(string studentId)
        {
            var result = await _service.DeleteStudentAsync(studentId);
            return Ok(new { message = result });
        }

        [HttpGet("DownloadExcelTemplate")]
        public async Task<IActionResult> DownloadExcelTemplate([FromQuery] StudExcelDto dto)
        {
            var (fileBytes, fileName) = await _service.GenerateExcelTemplateAsync(dto);

            return File(
                fileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName + ".xlsx"
            );
        }
        [HttpPost("ImportStudents")]
        public async Task<IActionResult> ImportStudents([FromForm] StudentImportDto dto)
        {
            var result = await _service.ImportStudentsAsync(dto);
            return Ok(new { message = result });
        }
        [HttpPost("ExamDetails")]
        public async Task<IActionResult> ExamDetails(string studentId)
        {
            var exams = await _service.GetExamDetailsAsync(studentId);
            return Ok(exams);
        }

        [HttpPut("RestoreExam")]
        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        public async Task<IActionResult> RestoreExam(string studentId, Guid marksId)
        {
            var result = await _service.RestoreExamAsync(studentId, marksId);
            return Ok(result);
        }

        [HttpDelete("DeleteExam")]
        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        public async Task<IActionResult> DeleteExam(string studentId, Guid marksId)
        {
            var result = await _service.DeleteExamAsync(studentId, marksId);
            return Ok(result);
        }

    }
}
