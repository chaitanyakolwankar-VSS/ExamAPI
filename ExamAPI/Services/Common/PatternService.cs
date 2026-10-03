using ExamAPI.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Services.Common
{
    [Route("api/[controller]")]
    [ApiController]
    public class PatternService : ControllerBase
    {
        public readonly ApplicationDbContext _Context;

        public PatternService(ApplicationDbContext context)
        {
            _Context = context;
        }
        // Duplicate of /api/Lookup/bootstrap (patterns); kept for team branches, remove after they migrate (T-19 D).
        [Obsolete("Use /api/Lookup/bootstrap (patterns) -- kept for team branches; remove after they migrate (T-19 D)")]
        [HttpGet]
        public IActionResult GetPattern()
        {
            var pattern = _Context.PatternMasters.Select(p => new { patternId = p.PatternId, patternName = p.PatternName });
            return Ok(pattern.ToList());
        }
    }
}
