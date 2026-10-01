using Microsoft.AspNetCore.StaticFiles;

namespace ExamAPI.Services.Files
{
    /// <summary>
    /// Persistent, non-public storage for uploaded student photos/signatures and college
    /// logos/banners (DB-07 / DEC-12).
    /// <para>
    /// Files live under a configurable root (<c>Storage:UploadsRoot</c>) that is outside
    /// <c>wwwroot</c> and is never served by static-file middleware. They are only reachable
    /// through the authorised <c>GET /api/Files</c> endpoint. Point the root at an absolute path
    /// outside the publish folder so a redeploy cannot overwrite it.
    /// </para>
    /// <para>
    /// The value stored in the database is a "stored path": a relative key such as
    /// <c>students/{guid}_photo.png</c> or <c>college/logos/{guid}_x.png</c>. Rows written before
    /// this change hold legacy keys (<c>/uploads/x.png</c>, <c>/Clg_details/logos/x.png</c>,
    /// <c>/Clg_detail/logos/x.png</c>); those are still resolved, first under the new root and then
    /// under the old <c>wwwroot</c> location.
    /// </para>
    /// </summary>
    public interface IFileStorage
    {
        /// <summary>Absolute path of the storage root.</summary>
        string Root { get; }

        /// <summary>Writes the bytes under <paramref name="subFolder"/> and returns the stored path to persist.</summary>
        Task<string> SaveAsync(byte[] content, string subFolder, string fileName, CancellationToken ct = default);

        /// <summary>
        /// Absolute path of the file a stored path points at, or null when the path is unsafe
        /// (traversal, rooted, drive letters...) or the file does not exist in any known location.
        /// </summary>
        string? ResolveExisting(string? storedPath);

        /// <summary>Reads a stored file, or null when it is missing or the path is unsafe.</summary>
        Task<byte[]?> ReadAllBytesAsync(string? storedPath, CancellationToken ct = default);

        /// <summary>Deletes the file a stored path points at, if it resolves. Never throws.</summary>
        void Delete(string? storedPath);
    }

    public sealed class FileStorage : IFileStorage
    {
        public const string StudentsFolder = "students";
        public const string CollegeLogosFolder = "college/logos";
        public const string CollegeBannersFolder = "college/banners";

        /// <summary>Only images are ever served; anything else is refused.</summary>
        private static readonly FileExtensionContentTypeProvider ContentTypes = new();
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

        private static readonly char[] ForbiddenChars = { ':', '*', '?', '"', '<', '>', '|' };

        private readonly string _root;
        private readonly string[] _legacyRoots;

        public FileStorage(string contentRootPath, string? webRootPath, string? configuredUploadsRoot)
        {
            var configured = configuredUploadsRoot?.Trim();
            _root = Path.GetFullPath(string.IsNullOrEmpty(configured)
                ? Path.Combine(contentRootPath, "App_Data", "uploads")
                : Path.IsPathRooted(configured) ? configured : Path.Combine(contentRootPath, configured));

            var legacy = new List<string>();
            if (!string.IsNullOrEmpty(webRootPath)) legacy.Add(Path.GetFullPath(webRootPath));
            var conventional = Path.GetFullPath(Path.Combine(contentRootPath, "wwwroot"));
            if (!legacy.Contains(conventional, StringComparer.OrdinalIgnoreCase)) legacy.Add(conventional);
            _legacyRoots = legacy.ToArray();
        }

        public string Root => _root;

        public async Task<string> SaveAsync(byte[] content, string subFolder, string fileName, CancellationToken ct = default)
        {
            var folderKey = NormalizeKey(subFolder)
                ?? throw new ArgumentException("Invalid storage folder.", nameof(subFolder));
            var safeName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeName) || safeName != fileName || NormalizeKey(safeName) == null)
                throw new ArgumentException("Invalid file name.", nameof(fileName));

            var key = $"{folderKey}/{safeName}";
            var fullPath = ResolveUnder(_root, key)
                ?? throw new ArgumentException("Invalid storage path.");

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, content, ct);
            return key;
        }

        public string? ResolveExisting(string? storedPath)
        {
            var key = NormalizeKey(storedPath);
            if (key == null) return null;

            foreach (var (root, candidateKey) in Candidates(key))
            {
                var found = ResolveUnder(root, candidateKey);
                if (found != null && File.Exists(found)) return found;
            }
            return null;
        }

        public async Task<byte[]?> ReadAllBytesAsync(string? storedPath, CancellationToken ct = default)
        {
            var path = ResolveExisting(storedPath);
            return path == null ? null : await File.ReadAllBytesAsync(path, ct);
        }

        public void Delete(string? storedPath)
        {
            try
            {
                var path = ResolveExisting(storedPath);
                if (path != null) File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>The content type for a servable image, or null for anything that is not one.</summary>
        public static string? ImageContentType(string path)
        {
            var ext = Path.GetExtension(path);
            if (!ImageExtensions.Contains(ext)) return null;
            return ContentTypes.TryGetContentType(path, out var type) ? type : null;
        }

        /// <summary>
        /// Canonical form of a stored path: forward slashes, no leading slash. Null when unsafe:
        /// empty, contains "..", "." or empty segments, a drive/scheme colon, control characters or
        /// wildcard characters. Absolute URLs are therefore not stored paths.
        /// </summary>
        public static string? NormalizeKey(string? storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath)) return null;
            var key = storedPath.Trim().Replace('\\', '/').TrimStart('/');
            if (key.Length == 0) return null;

            foreach (var ch in key)
            {
                if (char.IsControl(ch) || Array.IndexOf(ForbiddenChars, ch) >= 0)
                    return null;
            }

            foreach (var segment in key.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.EndsWith('.') || segment.EndsWith(' '))
                    return null;
            }
            return key;
        }

        /// <summary>The (root, key) pairs to look in, most preferred first.</summary>
        private IEnumerable<(string Root, string Key)> Candidates(string key)
        {
            var parts = key.Split('/', 2);
            var head = parts[0];
            var rest = parts.Length > 1 ? parts[1] : string.Empty;
            var isLegacyStudent = head.Equals("uploads", StringComparison.OrdinalIgnoreCase);
            var isLegacyCollege = head.Equals("Clg_details", StringComparison.OrdinalIgnoreCase)
                                  || head.Equals("Clg_detail", StringComparison.OrdinalIgnoreCase);

            // 1. Legacy keys mapped onto the new layout (after the one-time copy on the server).
            if (isLegacyStudent) yield return (_root, $"{StudentsFolder}/{rest}");
            else if (isLegacyCollege) yield return (_root, $"college/{rest}");

            // 2. The key as-is under the new root (new-format keys, or a wholesale copy of wwwroot).
            yield return (_root, key);

            // 3. Legacy location under wwwroot. Create and Update used different folder spellings
            //    (Clg_details vs Clg_detail), so a stored value may not match where the file is.
            foreach (var legacyRoot in _legacyRoots)
            {
                yield return (legacyRoot, key);
                if (head.Equals("Clg_details", StringComparison.OrdinalIgnoreCase))
                    yield return (legacyRoot, $"Clg_detail/{rest}");
                else if (head.Equals("Clg_detail", StringComparison.OrdinalIgnoreCase))
                    yield return (legacyRoot, $"Clg_details/{rest}");
            }
        }

        /// <summary>Combines root and key and proves the result is still inside root.</summary>
        private static string? ResolveUnder(string root, string key)
        {
            var normalised = NormalizeKey(key);
            if (normalised == null) return null;

            var full = Path.GetFullPath(Path.Combine(root, normalised.Replace('/', Path.DirectorySeparatorChar)));
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            return full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
    }
}
