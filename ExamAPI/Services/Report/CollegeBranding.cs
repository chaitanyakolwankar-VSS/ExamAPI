using ExamAPI.Data;
using ExamAPI.Services.Files;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Report
{
    /// <summary>
    /// What every report prints in its header for the current college (DEC-14): name, address and,
    /// when College Details has one, the logo image. Reports draw the logo left of the name when
    /// <see cref="Logo"/> is present and fall back to the plain text header when it is null.
    /// </summary>
    public sealed class CollegeBrandingInfo
    {
        /// <summary>Never the literal "College Name Not Found": the college code, or empty, when the name is blank.</summary>
        public string Name { get; init; } = string.Empty;
        public string? Address { get; init; }

        /// <summary>Decodable image bytes of the college logo, or null when there is none / it is unreadable.</summary>
        public byte[]? Logo { get; init; }

        public bool HasLogo => Logo is { Length: > 0 };

        public static CollegeBrandingInfo Empty { get; } = new();
    }

    public static class CollegeBranding
    {
        /// <summary>Logos are validated to 2 MB on upload; refuse to load anything absurdly larger.</summary>
        private const int MaxLogoBytes = 4 * 1024 * 1024;

        /// <summary>
        /// The name to print: the college name, else its code, else an empty string. Reports must
        /// never print a "not found" placeholder as if it were the college's name.
        /// </summary>
        public static string DisplayName(string? name, string? collegeCode)
        {
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            if (!string.IsNullOrWhiteSpace(collegeCode)) return collegeCode.Trim();
            return string.Empty;
        }

        /// <summary>
        /// Loads name, address and logo for <paramref name="collegeId"/>. The logo is read through
        /// <paramref name="storage"/> (same resolution as the Files endpoint, including the legacy
        /// wwwroot fallback); a null storage, a missing file or an undecodable image simply yields
        /// no logo.
        /// </summary>
        public static async Task<CollegeBrandingInfo> LoadAsync(
            ApplicationDbContext context, IFileStorage? storage, Guid collegeId, CancellationToken ct = default)
        {
            var college = await context.Colleges.AsNoTracking()
                .Where(c => c.CollegeId == collegeId && !c.IsDeleted)
                .Select(c => new { c.Name, c.CollegeCode, c.Address, c.LogoUrl })
                .FirstOrDefaultAsync(ct);

            if (college == null) return CollegeBrandingInfo.Empty;

            return new CollegeBrandingInfo
            {
                Name = DisplayName(college.Name, college.CollegeCode),
                Address = string.IsNullOrWhiteSpace(college.Address) ? null : college.Address.Trim(),
                Logo = await ReadLogoAsync(storage, college.LogoUrl, ct)
            };
        }

        public static async Task<byte[]?> ReadLogoAsync(IFileStorage? storage, string? storedPath, CancellationToken ct = default)
        {
            if (storage == null || string.IsNullOrWhiteSpace(storedPath)) return null;

            try
            {
                var path = storage.ResolveExisting(storedPath);
                if (path == null || FileStorage.ImageContentType(path) == null) return null;
                if (new FileInfo(path).Length is 0 or > MaxLogoBytes) return null;

                var bytes = await File.ReadAllBytesAsync(path, ct);
                return IsDecodableImage(bytes) ? bytes : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// True when the renderer can decode the bytes. A corrupt logo must degrade to the text
        /// header rather than fail the whole PDF at render time.
        /// </summary>
        public static bool IsDecodableImage(byte[] bytes)
        {
            try
            {
                if (!ImageSize.TryRead(bytes, out _, out _)) return false;
                using var image = QuestPDF.Infrastructure.Image.FromBinaryData(bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

namespace ExamAPI.Services.Report
{
    /// <summary>Places the college logo on a worksheet as a floating picture.</summary>
    public static class ExcelBranding
    {
        /// <summary>
        /// Inserts the logo as a picture anchored at the given 1-based cell, scaled to fit
        /// <paramref name="maxWidthPx"/> x <paramref name="maxHeightPx"/> keeping its aspect ratio.
        /// A picture floats above the grid, so no cell, row or column moves. Returns false (and adds
        /// nothing) when there is no logo or the picture cannot be embedded.
        /// </summary>
        public static bool TryAddLogo(
            OfficeOpenXml.ExcelWorksheet sheet, byte[]? logo, int row = 1, int column = 1,
            int maxWidthPx = 96, int maxHeightPx = 48)
        {
            if (logo == null || logo.Length == 0) return false;

            try
            {

                if (!ImageSize.TryRead(logo, out var imageWidth, out var imageHeight))
                    return false;
                var scale = Math.Min((double)maxWidthPx / imageWidth, (double)maxHeightPx / imageHeight);
                var width = Math.Max(1, (int)Math.Round(imageWidth * scale));
                var height = Math.Max(1, (int)Math.Round(imageHeight * scale));

                using var stream = new MemoryStream(logo);
                var name = $"CollegeLogo_{Guid.NewGuid():N}";
                var picture = sheet.Drawings.AddPicture(name, stream);
                picture.SetPosition(row - 1, 2, column - 1, 2);
                picture.SetSize(width, height);
                return true;
            }
            catch
            {
                // A logo EPPlus cannot embed must not fail the export; the text header remains.
                return false;
            }
        }
    }
}

namespace ExamAPI.Services.Report
{
    /// <summary>Reads pixel dimensions from an image header (PNG, JPEG, GIF, BMP, WebP) without decoding it.</summary>
    public static class ImageSize
    {
        public static bool TryRead(byte[] b, out int width, out int height)
        {
            width = height = 0;
            if (b == null || b.Length < 26) return false;

            // PNG: 8-byte signature, then IHDR with big-endian width/height.
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            {
                width = ReadInt32BE(b, 16);
                height = ReadInt32BE(b, 20);
            }
            // GIF: "GIF8", little-endian 16-bit width/height at offset 6.
            else if (b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8')
            {
                width = b[6] | (b[7] << 8);
                height = b[8] | (b[9] << 8);
            }
            // BMP: "BM", little-endian 32-bit width/height at offset 18 (height may be negative).
            else if (b[0] == 'B' && b[1] == 'M')
            {
                width = BitConverter.ToInt32(b, 18);
                height = Math.Abs(BitConverter.ToInt32(b, 22));
            }
            // JPEG: walk the segments to the first start-of-frame marker.
            else if (b[0] == 0xFF && b[1] == 0xD8)
            {
                var i = 2;
                while (i + 9 < b.Length)
                {
                    if (b[i] != 0xFF) { i++; continue; }
                    var marker = b[i + 1];
                    if (marker == 0xFF) { i++; continue; }
                    if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }
                    var length = (b[i + 2] << 8) | b[i + 3];
                    if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    {
                        height = (b[i + 5] << 8) | b[i + 6];
                        width = (b[i + 7] << 8) | b[i + 8];
                        break;
                    }
                    if (length < 2) return false;
                    i += 2 + length;
                }
            }
            // WebP: RIFF....WEBP then a VP8 / VP8L / VP8X chunk.
            else if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
            {
                if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == 'X')
                {
                    width = 1 + (b[24] | (b[25] << 8) | (b[26] << 16));
                    height = 1 + (b[27] | (b[28] << 8) | (b[29] << 16));
                }
                else if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == 'L')
                {
                    var bits = b[21] | (b[22] << 8) | (b[23] << 16) | ((uint)b[24] << 24);
                    width = 1 + (int)(bits & 0x3FFF);
                    height = 1 + (int)((bits >> 14) & 0x3FFF);
                }
                else if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == ' ')
                {
                    width = (b[26] | (b[27] << 8)) & 0x3FFF;
                    height = (b[28] | (b[29] << 8)) & 0x3FFF;
                }
            }

            return width > 0 && height > 0 && width < 100_000 && height < 100_000;
        }

        private static int ReadInt32BE(byte[] b, int offset)
            => (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];
    }
}
