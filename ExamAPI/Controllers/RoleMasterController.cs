using ExamAPI.DTOs;
using ExamAPI.Services.Auth;
using ExamAPI.Services.Common;
using ExamAPI.Services.RoleMaster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = AccessPolicies.CollegeAdmin)]
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
        [HttpGet("Selectmodule")]
        public async Task<IActionResult> Selectmodule()
        {
            var model = await _service.GetPermissionsAsync();
            return Ok(model);
        }
  
        [HttpGet("GetRoleById")]
        public async Task<IActionResult> GetRoleById(Guid roleId)
        {
            var result = await _service.GetRoleByIdAsync(roleId);
            return Ok(result);
        }
        [HttpPost("SaveRole")]
        public async Task<IActionResult> SaveRole([FromBody] CreateRoleDto dto)
        {
            try { return Ok(await _service.SaveRoleAsync(dto)); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }
         
        [HttpPost("UpdateRole")]
        public async Task<IActionResult> UpdateRole([FromBody] CreateRoleDto dto)
        {
            try { return Ok(await _service.UpdateRoleAsync(dto)); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }
        [HttpDelete("DeleteRole")]
        public async Task<IActionResult> DeleteRole(Guid roleId)
        {
            try { return Ok(await _service.DeleteRoleAsync(roleId)); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

    }
}



