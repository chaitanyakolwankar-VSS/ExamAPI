using ExamAPI.DTOs;
using ExamAPI.Services.UsersMaster;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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
        public async Task<IActionResult> CreateUser([FromBody] CreateUserMasterDTO dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var result = await _service.CreateUserAsync(dto);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("GetInfo")]
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
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            var result = await _service.DeleteUserById(id);

            if (!result)
                return NotFound(new { message = "User Not Found" });

            return Ok(new { message = "User Deleted Successfully" });
        }

        [HttpPut("Update")]
        public async Task<IActionResult> UpdateUser([FromBody] UpdateUserMasterDTO dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _service.UpdateUserMaster(dto);

            if (!result)
                return NotFound(new { message = "User not found" });

            return Ok(new { message = "User Updated Successfully" });
        }
    }
}
