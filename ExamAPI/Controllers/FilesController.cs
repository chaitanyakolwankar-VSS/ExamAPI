using ExamAPI.Data;
using ExamAPI.Services.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Controllers
{
    /// <summary>
    /// The only way to read an uploaded student photo/signature or college logo/banner (DEC-12).
    /// The files are not under wwwroot and are never served as static files.
    /// <para>
    /// <c>GET /api/Files?path={storedPath}</c> where <c>storedPath</c> is exactly the value the API
    /// returned in <c>PhotoUrl</c>, <c>SignUrl</c>, <c>LogoUrl</c>, <c>BannerUrl</c> or the hall-ticket
    /// logo. A file is served only when a row in the caller's own college references it -- the
    /// tenant query filters on StudentMasters and Colleges do the scoping -- so an arbitrary path
    /// (or another college's file) is a plain 404.
    /// </para>
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class FilesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileStorage _storage;

        public FilesController(ApplicationDbContext context, IFileStorage storage)
        {
            _context = context;
            _storage = storage;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string? path, CancellationToken ct)
        {
            var key = FileStorage.NormalizeKey(path);
            if (key == null)
                return BadRequest(new { message = "Invalid file path." });

            // Stored values may carry a leading slash (legacy rows) or not (new rows).
            // A List (not an array): EF Core 9 cannot translate array.Contains under the C# 14 span overloads.
            var variants = new List<string> { key, "/" + key };

            var ownedByStudent = await _context.StudentMasters.AsNoTracking()
                .AnyAsync(s => variants.Contains(s.PhotoUrl!) || variants.Contains(s.SignUrl!), ct);

            var ownedByCollege = !ownedByStudent && await _context.Colleges.AsNoTracking()
                .AnyAsync(c => variants.Contains(c.LogoUrl!) || variants.Contains(c.LogoBannerUrl!), ct);

            if (!ownedByStudent && !ownedByCollege)
                return NotFound();

            var fullPath = _storage.ResolveExisting(key);
            if (fullPath == null)
                return NotFound();

            var contentType = FileStorage.ImageContentType(fullPath);
            if (contentType == null)
                return NotFound();

            Response.Headers["Cache-Control"] = "private, max-age=300";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return PhysicalFile(fullPath, contentType);
        }
    }
}
