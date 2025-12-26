using ExamAPI.DTOs;
using ExamAPI.Services.Permissions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PermissionController : ControllerBase
    {
        private readonly IPermissionService _permissionService;

        public PermissionController(IPermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] PermissionCreate dto)
        {
            var result = await _permissionService.CreatePermissionAsync(dto);
            if (!result)
                return BadRequest("Permission not created");

            return Ok();
        }

        [HttpGet("modules")]
        public async Task<IActionResult> GetModules()
        {
            var modules = await _permissionService.GetModulesAsync();
            return Ok(modules);
        }

        [HttpGet("grouped")]
        public async Task<IActionResult> GetGroupedPermissions()
        {
            var data = await _permissionService.GetGroupedPermissionsAsync();
            return Ok(data);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, PermissionUpdate dto)
        {
            var result = await _permissionService.UpdatePermissionAsync(id, dto);
            return result ? Ok() : NotFound();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var result = await _permissionService.DeletePermissionAsync(id);
            return result ? Ok() : NotFound();
        }


    }
}
