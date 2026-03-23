
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Services.Common
{
    [Route("api/[controller]")]
    [ApiController]
    public class CourseService : ControllerBase
    {
        public readonly ApplicationDbContext _context;

        public CourseService(ApplicationDbContext courses)
        {
            _context =courses;
        }
        [HttpGet]
        public IActionResult GetCourses()
        {
            var courses = _context.CourseMasters.Select(c=>new { Courseid = c.CourseId, Coursename = c.Name});
            return Ok(courses.ToList());
        }
    }
}
