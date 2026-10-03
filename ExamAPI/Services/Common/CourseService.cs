
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
        // Duplicate of /api/Lookup/bootstrap (courses); kept for team branches, remove after they migrate (T-19 D).
        [Obsolete("Use /api/Lookup/bootstrap (courses) -- kept for team branches; remove after they migrate (T-19 D)")]
        [HttpGet]
        public IActionResult GetCourses()
        {
            var courses = _context.CourseMasters.Select(c=>new { Courseid = c.CourseId, Coursename = c.Name});
            return Ok(courses.ToList());
        }
    }
}
