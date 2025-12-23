using ExamAPI.Services.Common;
using ExamAPI.Services.RoleMaster;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RoleMasterController : ControllerBase
    {
        private readonly IRoleMasterService _service;
        public RoleMasterController(IRoleMasterService service)
        {
            _service = service;
        }
        [HttpGet("GetInfo")]
        public async Task<IActionResult> GetInfo()
        {
            var role = await _service.GetRoleAsync();
            return Ok(role);
        }
    }
}



