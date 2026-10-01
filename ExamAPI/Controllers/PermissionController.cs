using ExamAPI.DTOs;
using ExamAPI.Services.Auth;
using ExamAPI.Services.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    // The policy is per action, not on the class: GET me must stay open to every signed-in user, and a
    // class-level policy would also apply to it. A new action here gets only the global fallback
    // (authenticated) - give it an explicit policy.
    [Route("api/[controller]")]
    [ApiController]
    public class PermissionController : ControllerBase
    {
        private readonly IPermissionService _permissionService;

        public PermissionController(IPermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        /// <summary>
        /// The forms the signed-in user may open (role permissions + user permissions). Read on every page
        /// load so a changed role takes effect without re-login. Drives the menu and route guard (UX);
        /// per-form enforcement on the endpoints is a later step.
        /// </summary>
        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            return Ok(await _permissionService.GetMyAllowedFormsAsync());
        }

        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] PermissionCreate dto)
        {
            var result = await _permissionService.CreatePermissionAsync(dto);
            if (!result)
                return BadRequest("Permission not created");

            return Ok();
        }

        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        [HttpGet("modules")]
        public async Task<IActionResult> GetModules()
        {
            var modules = await _permissionService.GetModulesAsync();
            return Ok(modules);
        }

        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        [HttpGet("grouped")]
        public async Task<IActionResult> GetGroupedPermissions()
        {
            var data = await _permissionService.GetGroupedPermissionsAsync();
            return Ok(data);
        }

        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, PermissionUpdate dto)
        {
            var result = await _permissionService.UpdatePermissionAsync(id, dto);
            return result ? Ok() : NotFound();
        }

        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var result = await _permissionService.DeletePermissionAsync(id);
            return result ? Ok() : NotFound();
        }
    }
}
