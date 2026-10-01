using ExamAPI.DTOs;
using ExamAPI.Services.Auth;
using ExamAPI.Services.UsersMaster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserMasterController : ControllerBase
    {
        private readonly IUserMasterService _service;

        public UserMasterController(IUserMasterService service)
        {
            _service = service;
        }

        [HttpPost]
        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserMasterDTO dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // The new user is created inside the CALLER's college. For a college admin CollegeId
            // comes from the token and is never accepted from the request body. A platform admin
            // has no college of their own, so they name the target college in the body -- this is
            // how college admins get created (DEC-17).
            var isPlatformAdmin = AccessPolicies.IsPlatformAdmin(User);
            Guid collegeId;
            if (isPlatformAdmin)
            {
                if (dto.CollegeId is not Guid target || target == Guid.Empty)
                    return BadRequest(new { message = "CollegeId is required when a platform admin creates a user." });
                collegeId = target;
            }
            else
            {
                var collegeIdClaim = User.FindFirstValue("CollegeId");
                if (string.IsNullOrEmpty(collegeIdClaim) || !Guid.TryParse(collegeIdClaim, out collegeId))
                {
                    return Unauthorized(new { message = "Invalid or missing CollegeId in token." });
                }
            }

            try
            {
                var result = await _service.CreateUserAsync(dto, collegeId, isPlatformAdmin);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("GetInfo")]
        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _service.GetAllUsersAsync();
            return Ok(users);
        }

        [HttpGet("GetAll/{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var user = await _service.GetById(id);
            if (user == null)
                return NotFound();

            return Ok(user);
        }

        [HttpDelete("DeleteUser/{id}")]
        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            bool result;
            try
            {
                result = await _service.DeleteUserById(id, AccessPolicies.IsPlatformAdmin(User));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            if (!result)
                return NotFound(new { message = "User Not Found" });

            return Ok(new { message = "User Deleted Successfully" });
        }

        [HttpPut("Update")]
        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        public async Task<IActionResult> UpdateUser([FromBody] UpdateUserMasterDTO dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            bool result;
            try
            {
                result = await _service.UpdateUserMaster(dto, AccessPolicies.IsPlatformAdmin(User));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            if (!result)
                return NotFound(new { message = "User not found" });

            return Ok(new { message = "User Updated Successfully" });
        }

        [HttpPost("ChangePassword")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDTO dto)
        {
            try
            {
                await _service.ChangePasswordAsync(dto);
                return Ok(new {message="Password Updated Successfully"});
            }
            catch(Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
