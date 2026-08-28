using ExamAPI.DTOs;
using ExamAPI.Services.StatisticalReport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ExamAPI.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public sealed class StatisticalReportController : ControllerBase
{
    private readonly IStatisticalReportService _service;

    public StatisticalReportController(IStatisticalReportService service) => _service = service;

    [HttpPost("data")]
    public async Task<IActionResult> GetData([FromBody] StatisticalReportRequestDto request)
    {
        if (!TryGetCollegeId(out var collegeId)) return Unauthorized();
        var response = await _service.GetReportAsync(request, collegeId);
        return response.Success ? Ok(response) : BadRequest(response);
    }

    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] StatisticalReportRequestDto request)
    {
        if (!TryGetCollegeId(out var collegeId)) return Unauthorized();
        var response = await _service.GenerateExcelAsync(request, collegeId);
        return response.Success && response.Data != null
            ? File(response.Data, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"StatisticalReport_{request.ExamId}.xlsx")
            : BadRequest(response);
    }

    private bool TryGetCollegeId(out Guid collegeId) => Guid.TryParse(User.FindFirstValue("CollegeId"), out collegeId);
}
