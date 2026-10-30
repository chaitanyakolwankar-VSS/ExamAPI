using ClosedXML.Excel;
using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.EMMA;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.StudentAccess;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Data;
using System.Globalization;





namespace ExamAPI.Services.StudentMasters
{
    public class StudentMasterService : IStudentMasterService
    {

        private readonly ApplicationDbContext _context;
        private readonly ExamAPI.Services.Files.IFileStorage _storage;

        public StudentMasterService(ApplicationDbContext context, ExamAPI.Services.Files.IFileStorage storage)
        {
            _context = context;
            _storage = storage;
        }

        /// <summary>
        /// Student ID = 2-digit year + one running 5-digit number for the whole college, e.g. 2600001
        /// (year from the academic year "2026-27"; no branch part). The number continues from the highest
        /// 7-digit ID with the same year in this college -- deleted students included, since
        /// (CollegeId, StudentId) is unique. Older-style IDs (e.g. 26IL0001) are ignored.
        /// </summary>
        private async Task<(string Prefix, int Next)> NextStudentIdAsync(Guid ayid)
        {
            var shortDuration = await _context.AcademicYears
                .Where(a => a.AYID == ayid)
                .Select(a => a.ShortDuration)
                .FirstOrDefaultAsync();
            var yearDigits = new string((shortDuration ?? "").TakeWhile(char.IsDigit).ToArray());
            if (yearDigits.Length < 2)
                throw new InvalidOperationException("The academic year has no year in its label (e.g. 2026-27).");
            var prefix = yearDigits.Length >= 4 ? yearDigits.Substring(2, 2) : yearDigits.Substring(0, 2);

            var collegeId = _context.CurrentCollegeId;
            var existing = await _context.StudentMasters
                .IgnoreQueryFilters()
                .Where(s => s.CollegeId == collegeId && s.StudentId.StartsWith(prefix) && s.StudentId.Length == 7)
                .Select(s => s.StudentId)
                .ToListAsync();

            var highest = existing
                .Where(id => id.All(char.IsAsciiDigit))
                .Select(id => int.Parse(id[prefix.Length..]))
                .DefaultIfEmpty(0)
                .Max();
            return (prefix, highest + 1);
        }

        private static string FormatStudentId(string prefix, int number)
        {
            if (number > 99999)
                throw new InvalidOperationException("No student numbers left for this academic year.");
            return $"{prefix}{number:D5}";
        }

        private async Task<string> GenerateStudentIdAsync(Guid ayid)
        {
            var (prefix, next) = await NextStudentIdAsync(ayid);
            return FormatStudentId(prefix, next);
        }
        public async Task<List<FetchData>> GetbycourseAsync(Guid courseId, Guid ayid)
        {
            var data = await (
                from se in _context.StudentEligibilities
                join sm in _context.StudentMasters on se.StudentId equals sm.StudentId
                join cm in _context.CourseMasters on se.CourseId equals cm.CourseId
                where se.CourseId == courseId
                      && se.AYID == ayid
                orderby sm.StudentId descending
                select new FetchData
                {
                    Name = cm.Name,
                    StudentId = sm.StudentId,
                    FirstName = sm.FirstName,
                    LastName = sm.LastName,
                    MiddleName = sm.MiddleName,
                    MotherName = sm.MotherName,
                    StudentType = sm.StudentType,
                    SemesterId = se.SemesterId,
                    StudentPRN = sm.StudentPRN,
                    AbcId = sm.AbcId,
                    Phone = sm.Phone,
                    Dyslexia = sm.Dyslexia,
                    PhotoUrl = sm.PhotoUrl,
                    SignUrl = sm.SignUrl
                }
            ).ToListAsync();

            foreach (var row in data) row.StudentName = StudentNames.Display(row.LastName, row.FirstName, row.MiddleName, row.MotherName);
            return data;
        }
        public async Task<List<FetchData>> SearchStudentsAsync(Searchbyname model, Guid ayid)
        {
            var query = from se in _context.StudentEligibilities
                        join sm in _context.StudentMasters on se.StudentId equals sm.StudentId
                        join cm in _context.CourseMasters on se.CourseId equals cm.CourseId
                        where se.AYID == ayid
                        select new { se, sm, cm };

            if (!string.IsNullOrEmpty(model.StudentId))
                query = query.Where(x => x.sm.StudentId.Contains(model.StudentId));

            if (!string.IsNullOrEmpty(model.FirstName))
                query = query.Where(x => x.sm.FirstName.StartsWith(model.FirstName));

            if (!string.IsNullOrEmpty(model.MiddleName))
                query = query.Where(x => x.sm.MiddleName != null && x.sm.MiddleName.StartsWith(model.MiddleName));

            if (!string.IsNullOrEmpty(model.LastName))
                query = query.Where(x => x.sm.LastName.StartsWith(model.LastName));

            if (!string.IsNullOrEmpty(model.StudentPRN))
                query = query.Where(x => x.sm.StudentPRN.Contains(model.StudentPRN));


            var data = await query
                .OrderByDescending(x => x.sm.StudentId)
                .Select(x => new FetchData
                {
                    Name = x.cm.Name,
                    StudentId = x.sm.StudentId,
                    FirstName = x.sm.FirstName,
                    MiddleName = x.sm.MiddleName,
                    LastName = x.sm.LastName,
                    MotherName = x.sm.MotherName,
                    StudentType = x.sm.StudentType,
                    SemesterId = x.se.SemesterId,
                    Pattern = x.se.Pattern,
                    StudentPRN = x.sm.StudentPRN,
                    AbcId = x.sm.AbcId,
                    Phone = x.sm.Phone,
                    AYID = x.se.AYID ?? Guid.Empty,
                    PhotoUrl = x.sm.PhotoUrl,
                    SignUrl = x.sm.SignUrl
                })
                .ToListAsync();

            foreach (var row in data) row.StudentName = StudentNames.Display(row.LastName, row.FirstName, row.MiddleName, row.MotherName);
            return data;
        }
        public async Task<Savedata> GetStudentByIdAsync(string studentId, Guid ayid)
        {
            var data = await (
                from sm in _context.StudentMasters
                join se in _context.StudentEligibilities
                    on sm.StudentId equals se.StudentId
                where sm.StudentId == studentId
                && se.AYID == ayid
                && !sm.IsDeleted
                select new Savedata
                {
                    StudentId = sm.StudentId,
                    CourseId = se.CourseId.Value,
                    SemesterId = se.SemesterId,
                    Pattern = se.Pattern,
                    FirstName = sm.FirstName,
                    MiddleName = sm.MiddleName,
                    LastName = sm.LastName,
                    MotherName = sm.MotherName,
                    StudentType = sm.StudentType,
                    Gender = sm.Gender,
                    Category = sm.Category,
                    StudentPRN = sm.StudentPRN,
                    AbcId = sm.AbcId,
                    Phone = sm.Phone,
                    DateOfBirth = sm.DateOfBirth,
                    Address = sm.Address,
                    PhotoUrl = sm.PhotoUrl,
                    SignUrl = sm.SignUrl,
                    Dyslexia = sm.Dyslexia,
                    Email = sm.Email,
                    EmailStatus = sm.EmailStatus,
                    EmailFlagReason = sm.EmailFlagReason
                }
            ).FirstOrDefaultAsync();

            return data;
        }
        public async Task<string> UpdateStudentAsync(Savedata dto)
        {
            // 1️⃣ Find Student
            var student = await _context.StudentMasters
                .FirstOrDefaultAsync(x => x.StudentId == dto.StudentId && !x.IsDeleted);

            if (student == null)
                throw new InvalidOperationException("Student not found.");
            if (string.IsNullOrWhiteSpace(dto.Pattern))
                throw new InvalidOperationException("Select the pattern (e.g. NEP).");
            var motherName = CleanMotherName(dto.MotherName);
            var studentType = NormalizeStudentType(dto.StudentType) ?? throw new InvalidOperationException(StudentTypeRequired);
            // Only a typed ERN can clash: a blank one used to match every other student without an ERN.
            var pnrExists = await _context.StudentMasters
                .AnyAsync(x => !string.IsNullOrWhiteSpace(dto.StudentPRN) && x.StudentPRN == dto.StudentPRN && x.StudentId != dto.StudentId && !x.IsDeleted);

            if (pnrExists)
            {
                throw new InvalidOperationException("ERN already exists.");
            }
            var abcId = CleanAbcId(dto.AbcId);
            if (await AbcIdTakenAsync(_context, abcId, student.StdMstId))
                throw new InvalidOperationException("This ABC ID belongs to another student.");
            // Date of birth, phone and address are optional. A value that is already on record is kept as it is
            // (not re-checked), so an older record can still be saved for another change.
            var dateOfBirth = dto.DateOfBirth?.Date == student.DateOfBirth?.Date ? student.DateOfBirth : CleanDateOfBirth(dto.DateOfBirth);
            var phone = (dto.Phone ?? "").Trim() == (student.Phone ?? "") ? student.Phone : StudentDetailsService.Phone(dto.Phone);
            var address = (dto.Address ?? "").Trim() == (student.Address ?? "") ? student.Address : CleanAddress(dto.Address);

            // Email: optional here (students saved before it was asked have none); checked like the import.
            var email = StudentEmail.Normalize(dto.Email);
            if (email != (student.Email ?? ""))
            {
                await CheckSingleEmailAsync(email, required: false, student.StudentId);
                var old = student.Email;
                StudentEmail.Apply(student, email, await StudentEmail.AllowedDomainsAsync(_context));
                _context.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    Action = "UPDATE",
                    TableName = nameof(Models.StudentMaster),
                    RecordId = student.StdMstId.ToString(),
                    PerformedBy = _context.CurrentUserId,
                    UserType = "Staff",
                    Event = "EmailSet",
                    Details = $"Student {student.StudentId}: email {old ?? "(none)"} -> {(email == "" ? "(none)" : email)} on Student Master."
                });
            }
            // 2️⃣ Update basic fields

            student.FirstName = dto.FirstName;
            student.MiddleName = dto.MiddleName;
            student.LastName = dto.LastName?.Trim() ?? "";
            student.MotherName = motherName;
            student.StudentType = studentType;
            student.Category = dto.Category;
            student.StudentPRN = dto.StudentPRN;
            student.AbcId = abcId;
            student.DateOfBirth = dateOfBirth;
            student.Phone = phone;
            student.Address = address;
            student.Gender = dto.Gender;
            student.Dyslexia = dto.Dyslexia;

            // PHOTO UPDATE
            if (!string.IsNullOrWhiteSpace(dto.PhotoUrl))
            {
                if (dto.PhotoUrl.StartsWith("data:image"))
                {
                    Guid imageGuid = Guid.NewGuid();
                    string photoFileName = $"{imageGuid}_photo.png";

                    student.PhotoUrl = await SaveBase64ImageAsync(dto.PhotoUrl, photoFileName);
                }
            }

            if (!string.IsNullOrWhiteSpace(dto.SignUrl))
            {
                if (dto.SignUrl.StartsWith("data:image"))
                {
                    Guid imageGuid = Guid.NewGuid();
                    string signFileName = $"{imageGuid}_sign.png";

                    student.SignUrl = await SaveBase64ImageAsync(dto.SignUrl, signFileName);
                }
            }
            // 5️⃣ Update Eligibility: the enrolment of the selected academic year (a promoted student has one per year).
            var eligibility = await _context.StudentEligibilities
                .FirstOrDefaultAsync(x => x.StudentId == dto.StudentId && x.AYID == dto.AYID && !x.IsDeleted)
                ?? await _context.StudentEligibilities.FirstOrDefaultAsync(x => x.StudentId == dto.StudentId && !x.IsDeleted);

            if (eligibility != null)
            {
                eligibility.CourseId = dto.CourseId;
                eligibility.SemesterId = dto.SemesterId;
                eligibility.Pattern = dto.Pattern;
            }

            await _context.SaveChangesAsync();

            return "Student updated successfully";
        }
        /// <summary>The trimmed mother's name; throws when it is blank or over 50 characters.</summary>
        /// <summary>ABC ID (Academic Bank of Credits): 12 digits; spaces and dashes are dropped; blank = none.</summary>
        public static string? CleanAbcId(string? value)
        {
            var digits = new string((value ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
            if (digits.Length == 0) return null;
            if (digits.Length != 12 || !digits.All(char.IsAsciiDigit))
                throw new InvalidOperationException("ABC ID: enter the 12-digit number.");
            return digits;
        }

        /// <summary>True when another student of the college already has this ABC ID.</summary>
        public static Task<bool> AbcIdTakenAsync(ApplicationDbContext context, string? abcId, Guid? exceptStdMstId) =>
            abcId == null
                ? Task.FromResult(false)
                : context.StudentMasters.AnyAsync(x => x.AbcId == abcId && x.StdMstId != exceptStdMstId && !x.IsDeleted);

        /// <summary>
        /// Date of birth (date only), same rule as the student link: not in the future, and between 14 and 80 years old.
        /// Null = none.
        /// </summary>
        public static DateTime? CleanDateOfBirth(DateTime? value)
        {
            if (value == null) return null;
            var date = value.Value.Date;
            if (date > DateTime.Today)
                throw new InvalidOperationException("Date of birth cannot be in the future.");
            if (date > DateTime.Today.AddYears(-14) || date < DateTime.Today.AddYears(-80))
                throw new InvalidOperationException("Date of birth: check the year.");
            return date;
        }

        public const int MaxAddressLength = 300;

        /// <summary>Address with extra spaces and line breaks squeezed to single spaces; at most 300 characters; blank = none.</summary>
        public static string? CleanAddress(string? value)
        {
            var address = string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (address.Length == 0) return null;
            if (address.Length > MaxAddressLength)
                throw new InvalidOperationException($"Address: at most {MaxAddressLength} characters.");
            return address;
        }

        /// <summary>One filled row of the student sheet, as text; Dob is dd-MM-yyyy (or whatever the sheet had typed).</summary>
        private sealed record ImportedRow(int Row, string First, string Middle, string Last, string Mother, string Category, string Prn, string Gender, string Type,
            string Email, string AbcId, string Dob, string Phone, string Address);

        /// <summary>The date of birth cell as text: a real Excel date becomes dd-MM-yyyy, anything typed is kept as typed.</summary>
        private static string DateOfBirthText(IXLCell cell)
        {
            if (cell.DataType == XLDataType.DateTime)
                return cell.GetDateTime().ToString(DateOfBirthFormat, CultureInfo.InvariantCulture);
            if (cell.DataType == XLDataType.Number && cell.GetDouble() is > 1 and < 2958465) // an Excel date serial, even in a cell formatted as text
                return DateTime.FromOADate(cell.GetDouble()).ToString(DateOfBirthFormat, CultureInfo.InvariantCulture);
            return cell.GetFormattedString().Trim();
        }

        /// <summary>The date typed as dd-MM-yyyy (also d-M-yyyy, dd/MM/yyyy, yyyy-MM-dd), checked like <see cref="CleanDateOfBirth"/>; blank = none.</summary>
        private static DateTime? ParseDateOfBirth(string text)
        {
            if (text == "") return null;
            if (!DateTime.TryParseExact(text, DateOfBirthFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidOperationException($"Date of birth \"{text}\": type it as dd-MM-yyyy, e.g. 25-03-2005.");
            return CleanDateOfBirth(date);
        }

        private static string CleanMotherName(string? value)
        {
            var name = (value ?? "").Trim();
            if (name == "")
                throw new InvalidOperationException("Enter the mother's name.");
            if (name.Length > 50)
                throw new InvalidOperationException("The mother's name can be at most 50 characters.");
            return name;
        }

        private const string StudentTypeRequired = "Choose the student type (Engineering, Diploma or MCA).";

        /// <summary>"E", "D" or "M" for a student type given as the letter or the word in any case ("diploma" -> "D"); null when blank or anything else.</summary>
        public static string? NormalizeStudentType(string? value) => (value ?? "").Trim().ToUpperInvariant() switch
        {
            "E" or "ENGINEERING" => "E",
            "D" or "DIPLOMA" => "D",
            "M" or "MCA" => "M",
            _ => null,
        };

        /// <summary>Throws with what to fix when a normalized email cannot be saved for this student (null = a new one).</summary>
        private async Task CheckSingleEmailAsync(string email, bool required, string? ownStudentId)
        {
            var owners = await StudentEmail.OwnersAsync(_context, new[] { email });
            var problem = EmailProblem(email, required, new HashSet<string>(), owners, ownStudentId);
            if (problem != null)
                throw new InvalidOperationException(char.ToUpperInvariant(problem[0]) + problem[1..]);
        }

        public async Task<string> SaveStudentAsync(Savedata dto)
        {
            var motherName = CleanMotherName(dto.MotherName);
            var studentType = NormalizeStudentType(dto.StudentType) ?? throw new InvalidOperationException(StudentTypeRequired);
            var email = StudentEmail.Normalize(dto.Email);
            await CheckSingleEmailAsync(email, required: true, ownStudentId: null);
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var academicYear = await _context.AcademicYears
                    .Where(a => a.AYID == dto.AYID)
                    .Select(a => new { a.AYID, a.ShortDuration })
                    .FirstOrDefaultAsync();

                if (academicYear == null)
                    throw new Exception("No current academic year found.");

                // Exam assignment lists students by course + semester + PATTERN; without it the student
                // never shows up there.
                if (string.IsNullOrWhiteSpace(dto.Pattern))
                    throw new InvalidOperationException("Select the pattern (e.g. NEP).");

                Guid stdMstId = Guid.NewGuid();
                if (dto.CourseId == Guid.Empty || !await _context.CourseMasters.AnyAsync(c => c.CourseId == dto.CourseId))
                    throw new InvalidOperationException("Select a branch.");
                string studentId = await GenerateStudentIdAsync(dto.AYID);

                var pnrExists = await _context.StudentMasters
                    .AnyAsync(x => !string.IsNullOrWhiteSpace(dto.StudentPRN) && x.StudentPRN == dto.StudentPRN && x.StudentId != dto.StudentId && !x.IsDeleted);

                if (pnrExists)
                    throw new InvalidOperationException("ERN already exists");
                var abcId = CleanAbcId(dto.AbcId);
                if (await AbcIdTakenAsync(_context, abcId, null))
                    throw new InvalidOperationException("This ABC ID belongs to another student.");
                var dateOfBirth = CleanDateOfBirth(dto.DateOfBirth);
                var phone = StudentDetailsService.Phone(dto.Phone);
                var address = CleanAddress(dto.Address);


                var studentMaster = new Models.StudentMaster
                {
                    StdMstId = stdMstId,
                    StudentId = studentId,
                    FirstName = dto.FirstName,
                    MiddleName = dto.MiddleName,
                    LastName = dto.LastName?.Trim() ?? "",
                    MotherName = motherName,
                    StudentType = studentType,
                    Category = dto.Category,
                    StudentPRN=dto.StudentPRN,
                    AbcId = abcId,
                    DateOfBirth = dateOfBirth,
                    Phone = phone,
                    Address = address,
                    Gender = dto.Gender,
                    Dyslexia = dto.Dyslexia,
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                };
                StudentEmail.Apply(studentMaster, email, await StudentEmail.AllowedDomainsAsync(_context));

                // ✅ HANDLE IMAGE AFTER OBJECT CREATION
                if (!string.IsNullOrWhiteSpace(dto.PhotoUrl) && dto.PhotoUrl.StartsWith("data:image"))
                {
                    Guid imageGuid = Guid.NewGuid();
                    string photoFileName = $"{imageGuid}_photo.png";

                    studentMaster.PhotoUrl = await SaveBase64ImageAsync(dto.PhotoUrl, photoFileName);
                }

                if (!string.IsNullOrWhiteSpace(dto.SignUrl) && dto.SignUrl.StartsWith("data:image"))
                {
                    Guid imageGuid = Guid.NewGuid();
                    string signFileName = $"{imageGuid}_sign.png";

                    studentMaster.SignUrl = await SaveBase64ImageAsync(dto.SignUrl, signFileName);
                }

                _context.StudentMasters.Add(studentMaster);

                var studentEligibility = new StudentEligibility
                {
                    Id = Guid.NewGuid(),
                    StdMstId = stdMstId,
                    StudentId = studentId,
                    CourseId = dto.CourseId,
                    SemesterId = dto.SemesterId,
                    Pattern = dto.Pattern,
                    AYID = academicYear.AYID,
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                };

                _context.StudentEligibilities.Add(studentEligibility);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return studentId;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        /// <summary>
        /// Decodes a data-URL image and stores it in the persistent, non-public upload root
        /// (Storage:UploadsRoot). Returns the stored path to persist in PhotoUrl / SignUrl; the
        /// file is read back only through the authorised GET /api/Files endpoint.
        /// </summary>
        private const int MaxStudentImageBytes = 2 * 1024 * 1024;

        /// <summary>
        /// Sets the photo and/or signature of an existing student without touching any other field
        /// (UpdateStudent rewrites the whole record and its eligibility row). Old files are deleted.
        /// </summary>
        public async Task<(string? PhotoUrl, string? SignUrl)> UpdateImagesAsync(StudentImagesDto dto)
        {
            // Tenant-filtered: a student of another college is "not found".
            var student = await _context.StudentMasters
                .FirstOrDefaultAsync(x => x.StudentId == dto.StudentId && !x.IsDeleted)
                ?? throw new KeyNotFoundException("Student not found.");

            if (!string.IsNullOrWhiteSpace(dto.Photo))
            {
                var saved = await SaveBase64ImageAsync(dto.Photo, $"{Guid.NewGuid()}_photo.png");
                _storage.Delete(student.PhotoUrl);
                student.PhotoUrl = saved;
            }
            if (!string.IsNullOrWhiteSpace(dto.Sign))
            {
                var saved = await SaveBase64ImageAsync(dto.Sign, $"{Guid.NewGuid()}_sign.png");
                _storage.Delete(student.SignUrl);
                student.SignUrl = saved;
            }

            await _context.SaveChangesAsync();
            return (student.PhotoUrl, student.SignUrl);
        }

        private async Task<string?> SaveBase64ImageAsync(string? base64Data, string fileName)
        {
            if (string.IsNullOrEmpty(base64Data))
                return null;

            var commaIndex = base64Data.IndexOf(',');
            if (commaIndex >= 0)
                base64Data = base64Data.Substring(commaIndex + 1);

            byte[] imageBytes;
            try { imageBytes = Convert.FromBase64String(base64Data); }
            catch (FormatException) { throw new ArgumentException("The image could not be read."); }

            // Same limits as the college images: real, decodable images under 2 MB only.
            if (imageBytes.Length > MaxStudentImageBytes)
                throw new ArgumentException("The image must be less than 2 MB.");
            if (!ExamAPI.Services.Report.CollegeBranding.IsDecodableImage(imageBytes))
                throw new ArgumentException("The file is not a supported image.");

            return await _storage.SaveAsync(imageBytes, ExamAPI.Services.Files.FileStorage.StudentsFolder, fileName);
        }
        /// <summary>Columns of the student template, in order. Row 1 is a title, row 2 these headings, data from row 3.</summary>
        private static readonly string[] TemplateHeaders = { "FirstName", "MiddleName", "LastName", "MotherName", "Category", "ERN", "Gender", "StudentType", "Email", "AbcId", "DateOfBirth", "Phone", "Address" };
        /// <summary>Columns a template may lack (older templates): their cells read as empty.</summary>
        private static readonly string[] OptionalTemplateHeaders = { "AbcId", "DateOfBirth", "Phone", "Address" };
        private const string DateOfBirthFormat = "dd-MM-yyyy";
        private static readonly string[] DateOfBirthFormats = { "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" };
        // Templates downloaded before the ERN rename say "PRN" in that column; still accept them.
        private const string LegacyErnHeader = "PRN";
        private const int TemplateRows = 1000;

        private static int TemplateColumn(string header) => Array.IndexOf(TemplateHeaders, header) + 1;

        /// <summary>Admission categories (same list as the Student Master form; OPEN = general).</summary>
        public static readonly string[] StudentCategories =
            { "OPEN", "NT-1 (NT-B)", "NT-2 (NT-C)", "NT-3 (NT-D)", "OBC", "SBC", "SC", "ST", "VJ/DT(A)", "EBC", "SEBC", "TFWS" };

        /// <summary>The list's spelling of a category typed in any case ("obc" -> "OBC"), or null when it is not in the list.</summary>
        private static string? CanonicalCategory(string value) =>
            StudentCategories.FirstOrDefault(c => string.Equals(c, value.Trim(), StringComparison.OrdinalIgnoreCase));

        public async Task<(byte[] FileBytes, string FileName)> GenerateExcelTemplateAsync(StudExcelDto dto)
        {
            var course = await _context.CourseMasters
                .Where(c => c.CourseId == dto.CourseId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync();
            var academicYear = await _context.AcademicYears
                .Where(a => a.AYID == dto.AYID)
                .Select(a => a.ShortDuration)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(course))
                throw new InvalidOperationException("Select a branch.");
            if (string.IsNullOrEmpty(academicYear))
                throw new InvalidOperationException("Select an academic year.");
            if (string.IsNullOrWhiteSpace(dto.SemesterId) || string.IsNullOrWhiteSpace(dto.Pattern))
                throw new InvalidOperationException("Select the semester and the pattern.");

            using var workbook = ExamAPI.Services.Report.ExcelStyles.NewWorkbook();
            var sheet = workbook.Worksheets.Add("Students");

            // Row 1: what this sheet is for (the import takes branch/semester/pattern from the screen, not from here).
            sheet.Cell(1, 1).Value = $"{course}  |  {dto.SemesterId}  |  {dto.Pattern}  |  {academicYear}";
            sheet.Range(1, 1, 1, TemplateHeaders.Length).Merge();
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 13;
            sheet.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            for (int i = 0; i < TemplateHeaders.Length; i++)
            {
                var cell = sheet.Cell(2, i + 1);
                cell.Value = TemplateHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF9");
            }
            sheet.SheetView.FreezeRows(2);

            var data = sheet.Range(3, 1, TemplateRows + 2, TemplateHeaders.Length);
            data.Style.NumberFormat.Format = "@"; // text, so long ERNs are not turned into 1.23E+14

            // Gender: a drop-down with the two values the screen uses.
            var categories = sheet.Range(3, TemplateColumn("Category"), TemplateRows + 2, TemplateColumn("Category")).CreateDataValidation();
            categories.List("\"" + string.Join(",", StudentCategories) + "\"", true);
            categories.ErrorTitle = "Category";
            categories.ErrorMessage = "Choose a category from the list (or leave it empty).";

            var gender = sheet.Range(3, TemplateColumn("Gender"), TemplateRows + 2, TemplateColumn("Gender")).CreateDataValidation();
            gender.List("\"Male,Female\"", true);
            gender.ErrorTitle = "Gender";
            gender.ErrorMessage = "Choose Male or Female.";

            var studentType = sheet.Range(3, TemplateColumn("StudentType"), TemplateRows + 2, TemplateColumn("StudentType")).CreateDataValidation();
            studentType.List("\"E,D,M\"", true);
            studentType.ShowInputMessage = true;
            studentType.InputTitle = "Student type";
            studentType.InputMessage = "E = Engineering, D = Diploma, M = MCA";
            studentType.ErrorTitle = "Student type";
            studentType.ErrorMessage = "Enter E (Engineering), D (Diploma) or M (MCA).";

            // ERN: highlight a number typed twice in this sheet.
            sheet.Range(3, TemplateColumn("ERN"), TemplateRows + 2, TemplateColumn("ERN")).AddConditionalFormat().WhenIsDuplicate().Fill.SetBackgroundColor(XLColor.LightPink);

            var email = sheet.Range(3, TemplateColumn("Email"), TemplateRows + 2, TemplateColumn("Email"));
            email.AddConditionalFormat().WhenIsDuplicate().Fill.SetBackgroundColor(XLColor.LightPink);
            var emailHint = email.CreateDataValidation();
            emailHint.AllowedValues = XLAllowedValues.AnyValue;
            emailHint.ShowInputMessage = true;
            emailHint.InputTitle = "Email";
            emailHint.InputMessage = "Required. The student's own address: task links and OTPs are sent here.";

            // The last four columns are optional (the student can also fill them in through their link): a short hint when the cell is selected.
            void Hint(string header, string title, string message)
            {
                var validation = sheet.Range(3, TemplateColumn(header), TemplateRows + 2, TemplateColumn(header)).CreateDataValidation();
                validation.AllowedValues = XLAllowedValues.AnyValue;
                validation.ShowInputMessage = true;
                validation.InputTitle = title;
                validation.InputMessage = message;
            }
            Hint("AbcId", "ABC ID", "Optional. The 12-digit number (spaces and dashes are fine).");
            Hint("DateOfBirth", "Date of birth", "Optional. Type it as dd-MM-yyyy, e.g. 25-03-2005.");
            Hint("Phone", "Phone", "Optional. 10-digit mobile number.");
            Hint("Address", "Address", "Optional. At most 300 characters.");
            sheet.Range(3, TemplateColumn("AbcId"), TemplateRows + 2, TemplateColumn("AbcId")).AddConditionalFormat().WhenIsDuplicate().Fill.SetBackgroundColor(XLColor.LightPink);

            double[] widths = { 18, 18, 18, 18, 14, 20, 10, 12, 32, 16, 14, 14, 40 };
            for (int i = 0; i < widths.Length; i++) sheet.Column(i + 1).Width = widths[i];

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            // The controller adds ".xlsx".
            var fileName = string.Concat($"Students {course} {dto.SemesterId} {dto.Pattern} {academicYear}"
                .Split(Path.GetInvalidFileNameChars()));
            return (stream.ToArray(), fileName);
        }

        public async Task<StudentImportResult> ImportStudentsAsync(StudentImportDto dto)
        {
            if (dto.File == null || dto.File.Length == 0)
                throw new InvalidOperationException("Choose the filled-in template (.xlsx).");
            if (dto.CourseId == Guid.Empty || string.IsNullOrWhiteSpace(dto.SemesterId) || string.IsNullOrWhiteSpace(dto.Pattern))
                throw new InvalidOperationException("Select the branch, semester and pattern before importing.");

            var academicYear = await _context.AcademicYears
                .Where(a => a.AYID == dto.AYID)
                .Select(a => new { a.AYID, a.ShortDuration })
                .FirstOrDefaultAsync();
            if (academicYear == null)
                throw new InvalidOperationException("Select an academic year.");

            using var stream = new MemoryStream();
            await dto.File.CopyToAsync(stream);
            XLWorkbook workbook;
            try
            {
                stream.Position = 0;
                workbook = new XLWorkbook(stream);
            }
            catch
            {
                throw new InvalidOperationException("The file could not be read. Use the downloaded .xlsx template.");
            }

            using (workbook)
            {
                var sheet = workbook.Worksheet(1);
                // Columns are found by heading, so a template from before MotherName (or with reordered columns) still reads correctly.
                var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int lastColumn = sheet.Row(2).LastCellUsed()?.Address.ColumnNumber ?? 0;
                for (int c = 1; c <= lastColumn; c++)
                {
                    var heading = sheet.Cell(2, c).GetString().Trim();
                    if (heading.Equals(LegacyErnHeader, StringComparison.OrdinalIgnoreCase)) heading = "ERN";
                    columns.TryAdd(heading, c);
                }
                if (TemplateHeaders.Any(h => h != "MotherName" && h != "StudentType" && h != "Email" && !OptionalTemplateHeaders.Contains(h) && !columns.ContainsKey(h)))
                    throw new InvalidOperationException("This is not the student template. Download it again with the Excel button.");
                if (!columns.ContainsKey("MotherName"))
                    throw new InvalidOperationException("This template has no MotherName column. Download the template again.");
                if (!columns.ContainsKey("StudentType"))
                    throw new InvalidOperationException("This template has no StudentType column. Download the template again.");
                if (!columns.ContainsKey("Email"))
                    throw new InvalidOperationException("This template has no Email column (email is now required). Download the template again.");

                // Read every filled row first; nothing is saved unless all rows are valid.
                var rows = new List<ImportedRow>();
                int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
                for (int r = 3; r <= lastRow; r++)
                {
                    // A column the template does not have (an older template) reads as empty.
                    string Cell(string header) => columns.TryGetValue(header, out var c) ? sheet.Cell(r, c).GetFormattedString().Trim() : "";
                    var dobText = columns.TryGetValue("DateOfBirth", out var dobColumn) ? DateOfBirthText(sheet.Cell(r, dobColumn)) : "";
                    var row = new ImportedRow(r, Cell("FirstName"), Cell("MiddleName"), Cell("LastName"), Cell("MotherName"), Cell("Category"), Cell("ERN"), Cell("Gender"), Cell("StudentType"),
                        StudentEmail.Normalize(Cell("Email")), Cell("AbcId"), dobText, Cell("Phone"), Cell("Address"));
                    if (row.First == "" && row.Middle == "" && row.Last == "" && row.Mother == "" && row.Category == "" && row.Prn == "" && row.Gender == "" && row.Type == "" && row.Email == ""
                        && row.AbcId == "" && row.Dob == "" && row.Phone == "" && row.Address == "")
                        continue; // empty row
                    rows.Add(row);
                }
                if (rows.Count == 0)
                    throw new InvalidOperationException("The file has no students. Fill them in from row 3.");

                var existingPrns = await _context.StudentMasters
                    .Where(s => !s.IsDeleted && s.StudentPRN != null && s.StudentPRN != "")
                    .Select(s => s.StudentPRN!)
                    .ToHashSetAsync();
                var emailOwners = await StudentEmail.OwnersAsync(_context, rows.Select(r => r.Email));
                var allowedDomains = await StudentEmail.AllowedDomainsAsync(_context);
                var existingAbcIds = await _context.StudentMasters
                    .Where(s => !s.IsDeleted && s.AbcId != null && s.AbcId != "")
                    .Select(s => s.AbcId!)
                    .ToHashSetAsync();
                var prnsInFile = new HashSet<string>();
                var abcIdsInFile = new HashSet<string>();
                var emailsInFile = new HashSet<string>();
                var errors = new List<string>();
                var results = new List<ImportRowResult>();

                foreach (var (row, first, middle, last, mother, category, prn, gender, type, email, abcText, dobText, phoneText, addressText) in rows)
                {
                    var rowErrors = new List<string>();
                    if (first == "")
                        rowErrors.Add("first name is required.");
                    if (mother == "")
                        rowErrors.Add("mother's name is required.");
                    if (first.Length > 50 || middle.Length > 50 || last.Length > 50 || mother.Length > 50 || category.Length > 50 || prn.Length > 50)
                        rowErrors.Add("a value is longer than 50 characters.");
                    if (!string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase) && !string.Equals(gender, "Female", StringComparison.OrdinalIgnoreCase))
                        rowErrors.Add("gender must be Male or Female.");
                    if (type == "")
                        rowErrors.Add("student type is required (E, D or M).");
                    else if (NormalizeStudentType(type) == null)
                        rowErrors.Add("student type must be E, D or M.");
                    if (category != "" && CanonicalCategory(category) == null)
                        rowErrors.Add($"category \"{category}\" is not in the list (OPEN, OBC, SC, ST, ...).");
                    if (prn != "")
                    {
                        if (existingPrns.Contains(prn))
                            rowErrors.Add($"ERN {prn} already belongs to a student.");
                        else if (!prnsInFile.Add(prn))
                            rowErrors.Add($"ERN {prn} appears twice in this file.");
                    }
                    var emailError = EmailProblem(email, required: true, emailsInFile, emailOwners, ownStudentId: null);
                    if (emailError != null) rowErrors.Add(emailError);

                    // Optional columns: blank is fine; a typed value is checked like the Student Master form and the student link.
                    try
                    {
                        var abc = CleanAbcId(abcText);
                        if (abc != null)
                        {
                            if (existingAbcIds.Contains(abc))
                                rowErrors.Add($"ABC ID {abc} already belongs to a student.");
                            else if (!abcIdsInFile.Add(abc))
                                rowErrors.Add($"ABC ID {abc} appears twice in this file.");
                        }
                    }
                    catch (InvalidOperationException ex) { rowErrors.Add(ex.Message); }
                    try { ParseDateOfBirth(dobText); }
                    catch (InvalidOperationException ex) { rowErrors.Add(ex.Message); }
                    try { StudentDetailsService.Phone(phoneText); }
                    catch (InvalidOperationException ex) { rowErrors.Add(ex.Message); }
                    try { CleanAddress(addressText); }
                    catch (InvalidOperationException ex) { rowErrors.Add(ex.Message); }

                    errors.AddRange(rowErrors.Select(e => $"Row {row}: {e}"));
                    var flag = emailError == null ? StudentEmail.FlagReason(email, allowedDomains) : null;
                    results.Add(new ImportRowResult
                    {
                        Row = row,
                        Name = StudentNames.Display(last, first, middle, mother),
                        Email = email,
                        Status = rowErrors.Count > 0 ? ImportRowStatuses.Error : flag != null ? ImportRowStatuses.Flagged : ImportRowStatuses.Ok,
                        Note = rowErrors.Count > 0 ? string.Join(" ", rowErrors) : flag
                    });
                }

                var flagged = results.Count(r => r.Status == ImportRowStatuses.Flagged);
                if (errors.Count > 0)
                    return new StudentImportResult { Success = false, Message = $"Nothing was imported. Fix {errors.Count} problem(s) and import again.", Errors = errors, Rows = results };
                if (dto.Preview)
                    return new StudentImportResult
                    {
                        Success = true,
                        Message = $"{rows.Count} student(s) ready to import{(flagged > 0 ? $"; {flagged} email(s) flagged for checking" : "")}. Check the list, then press Save.",
                        Rows = results
                    };

                var (prefix, numberPart) = await NextStudentIdAsync(academicYear.AYID);

                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var resultIndex = 0;
                    foreach (var (_, first, middle, last, mother, category, prn, gender, type, email, abcText, dobText, phoneText, addressText) in rows)
                    {
                        var studentId = FormatStudentId(prefix, numberPart);
                        numberPart++;
                        var stdMstId = Guid.NewGuid();
                        results[resultIndex++].StudentId = studentId;

                        var student = new Models.StudentMaster
                        {
                            StdMstId = stdMstId,
                            StudentId = studentId,
                            FirstName = first,
                            MiddleName = middle == "" ? null : middle,
                            LastName = last,
                            MotherName = mother,
                            StudentType = NormalizeStudentType(type),
                            Category = category == "" ? null : CanonicalCategory(category),
                            StudentPRN = prn == "" ? null : prn,
                            AbcId = CleanAbcId(abcText),
                            DateOfBirth = ParseDateOfBirth(dobText),
                            Phone = StudentDetailsService.Phone(phoneText),
                            Address = CleanAddress(addressText),
                            Gender = string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase) ? "Male" : "Female",
                            Dyslexia = false,
                            CreatedAt = DateTime.UtcNow,
                            IsDeleted = false
                        };
                        StudentEmail.Apply(student, email, allowedDomains);
                        _context.StudentMasters.Add(student);
                        _context.StudentEligibilities.Add(new StudentEligibility
                        {
                            Id = Guid.NewGuid(),
                            StdMstId = stdMstId,
                            StudentId = studentId,
                            CourseId = dto.CourseId,
                            Pattern = dto.Pattern,
                            SemesterId = dto.SemesterId,
                            AYID = academicYear.AYID,
                            CreatedAt = DateTime.UtcNow,
                            IsDeleted = false
                        });
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return new StudentImportResult
                    {
                        Success = true,
                        Saved = true,
                        Message = $"{rows.Count} student(s) imported{(flagged > 0 ? $"; {flagged} email(s) flagged for checking" : "")}.",
                        Rows = results
                    };
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
        }
        /// <summary>
        /// What is wrong with an (already normalized) email, or null when it can be saved. Records it in
        /// <paramref name="emailsInFile"/> so a second row with the same email is caught.
        /// </summary>
        private static string? EmailProblem(string email, bool required, HashSet<string> emailsInFile, Dictionary<string, string> owners, string? ownStudentId)
        {
            if (email == "")
                return required ? "email is required." : null;
            if (!StudentEmail.IsValid(email))
                return $"\"{email}\" is not a valid email address.";
            if (!emailsInFile.Add(email))
                return $"email {email} appears twice in this file.";
            if (owners.TryGetValue(email, out var owner) && owner != ownStudentId)
                return $"email {email} already belongs to student {owner}.";
            return null;
        }

        private const string EmailSheetTitle = "STUDENT EMAILS";
        private static readonly string[] EmailSheetHeaders = { "StudentId", "Name", "ERN", "Email" };

        /// <summary>The students of a class, by name, as the email sheet lists them.</summary>
        private Task<List<Models.StudentMaster>> ClassStudentsAsync(Guid courseId, string semesterId, string pattern, Guid ayid) =>
            (from se in _context.StudentEligibilities
             join sm in _context.StudentMasters on se.StudentId equals sm.StudentId
             where se.CourseId == courseId && se.SemesterId == semesterId && se.Pattern == pattern && se.AYID == ayid
             select sm).Distinct().ToListAsync();

        /// <summary>
        /// Excel of a class's saved students for staff to fill in emails: Student ID, name and ERN are locked,
        /// only the Email column can be typed in (and no sheets added). Existing emails are filled in.
        /// </summary>
        public async Task<(byte[] FileBytes, string FileName)> GenerateEmailSheetAsync(StudExcelDto dto)
        {
            var course = await _context.CourseMasters.Where(c => c.CourseId == dto.CourseId).Select(c => c.Name).FirstOrDefaultAsync();
            var academicYear = await _context.AcademicYears.Where(a => a.AYID == dto.AYID).Select(a => a.ShortDuration).FirstOrDefaultAsync();
            if (string.IsNullOrEmpty(course))
                throw new InvalidOperationException("Select a branch.");
            if (string.IsNullOrEmpty(academicYear))
                throw new InvalidOperationException("Select an academic year.");
            if (string.IsNullOrWhiteSpace(dto.SemesterId) || string.IsNullOrWhiteSpace(dto.Pattern))
                throw new InvalidOperationException("Select the semester and the pattern.");

            var students = (await ClassStudentsAsync(dto.CourseId, dto.SemesterId, dto.Pattern, dto.AYID))
                .Select(s => new { s.StudentId, Name = StudentNames.Display(s), s.StudentPRN, s.Email })
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.StudentId, StringComparer.Ordinal)
                .ToList();
            if (students.Count == 0)
                throw new InvalidOperationException("No students are saved in this branch, semester and pattern yet.");

            using var workbook = ExamAPI.Services.Report.ExcelStyles.NewWorkbook();
            var sheet = workbook.Worksheets.Add("Emails");
            sheet.Cell(1, 1).Value = $"{EmailSheetTitle}  |  {course}  |  {dto.SemesterId}  |  {dto.Pattern}  |  {academicYear}";
            sheet.Range(1, 1, 1, EmailSheetHeaders.Length).Merge();
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 13;
            for (int i = 0; i < EmailSheetHeaders.Length; i++)
            {
                var cell = sheet.Cell(2, i + 1);
                cell.Value = EmailSheetHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF9");
            }
            sheet.SheetView.FreezeRows(2);

            int row = 3;
            foreach (var s in students)
            {
                sheet.Cell(row, 1).Value = s.StudentId;
                sheet.Cell(row, 2).Value = s.Name;
                sheet.Cell(row, 3).Value = s.StudentPRN ?? "";
                sheet.Cell(row, 4).Value = s.Email ?? "";
                row++;
            }
            var emails = sheet.Range(3, 4, row - 1, 4);
            emails.Style.NumberFormat.Format = "@";
            emails.Style.Fill.BackgroundColor = XLColor.FromArgb(255, 253, 208); // the column to fill in
            emails.Style.Protection.Locked = false;
            emails.AddConditionalFormat().WhenIsDuplicate().Fill.SetBackgroundColor(XLColor.LightPink);
            ExamAPI.Services.Report.ExcelStyles.ThinBorders(sheet.Range(2, 1, row - 1, EmailSheetHeaders.Length).Style, XLColor.Gray);

            double[] widths = { 12, 40, 20, 36 };
            for (int i = 0; i < widths.Length; i++) sheet.Column(i + 1).Width = widths[i];

            // Random password: nobody needs to unprotect it, they download a fresh sheet.
            var password = Guid.NewGuid().ToString("N");
            sheet.Protect(password);
            workbook.Protect(password);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileName = string.Concat($"Student emails {course} {dto.SemesterId} {dto.Pattern} {academicYear}".Split(Path.GetInvalidFileNameChars()));
            return (stream.ToArray(), fileName);
        }

        /// <summary>
        /// Reads the filled-in email sheet: matches each row by Student ID to a student of the selected class and
        /// sets the email. A blank email leaves the student as is. All rows are checked first and nothing is saved
        /// unless every row is right; with Preview only the per-row result is answered.
        /// </summary>
        public async Task<StudentImportResult> ImportEmailsAsync(StudentImportDto dto)
        {
            if (dto.File == null || dto.File.Length == 0)
                throw new InvalidOperationException("Choose the filled-in email sheet (.xlsx).");
            if (dto.CourseId == Guid.Empty || string.IsNullOrWhiteSpace(dto.SemesterId) || string.IsNullOrWhiteSpace(dto.Pattern))
                throw new InvalidOperationException("Select the branch, semester and pattern first.");

            using var stream = new MemoryStream();
            await dto.File.CopyToAsync(stream);
            XLWorkbook workbook;
            try
            {
                stream.Position = 0;
                workbook = new XLWorkbook(stream);
            }
            catch
            {
                throw new InvalidOperationException("The file could not be read. Use the downloaded email sheet.");
            }

            using (workbook)
            {
                var sheet = workbook.Worksheet(1);
                var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int lastColumn = sheet.Row(2).LastCellUsed()?.Address.ColumnNumber ?? 0;
                for (int c = 1; c <= lastColumn; c++)
                    columns.TryAdd(sheet.Cell(2, c).GetString().Trim(), c);
                if (!columns.ContainsKey("StudentId") || !columns.ContainsKey("Email"))
                    throw new InvalidOperationException("This is not the email sheet. Download it again with the button.");

                var classStudents = (await ClassStudentsAsync(dto.CourseId, dto.SemesterId, dto.Pattern, dto.AYID))
                    .ToDictionary(s => s.StudentId, StringComparer.OrdinalIgnoreCase);

                var rows = new List<(int Row, string StudentId, string Email)>();
                int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
                for (int r = 3; r <= lastRow; r++)
                {
                    var id = sheet.Cell(r, columns["StudentId"]).GetFormattedString().Trim();
                    var email = StudentEmail.Normalize(sheet.Cell(r, columns["Email"]).GetFormattedString());
                    if (id == "" && email == "") continue;
                    rows.Add((r, id, email));
                }
                if (rows.Count == 0)
                    throw new InvalidOperationException("The sheet has no students.");

                var owners = await StudentEmail.OwnersAsync(_context, rows.Select(r => r.Email));
                var allowedDomains = await StudentEmail.AllowedDomainsAsync(_context);
                var idsInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var emailsInFile = new HashSet<string>();
                var errors = new List<string>();
                var results = new List<ImportRowResult>();
                var changes = new List<(Models.StudentMaster Student, string Email)>();

                foreach (var (row, id, email) in rows)
                {
                    var result = new ImportRowResult { Row = row, StudentId = id, Email = email };
                    results.Add(result);
                    string? problem = null;
                    if (id == "")
                        problem = "the Student ID is empty.";
                    else if (!classStudents.TryGetValue(id, out var student))
                        problem = $"student {id} is not in this branch, semester and pattern.";
                    else if (!idsInFile.Add(id))
                        problem = $"student {id} appears twice in this file.";
                    else
                    {
                        result.Name = StudentNames.Display(student);
                        problem = EmailProblem(email, required: false, emailsInFile, owners, student.StudentId);
                        if (problem == null)
                        {
                            if (email == "")
                            {
                                result.Status = ImportRowStatuses.Skipped;
                                result.Note = "No email typed in; left as is.";
                            }
                            else if (email == student.Email)
                            {
                                result.Status = ImportRowStatuses.Unchanged;
                            }
                            else
                            {
                                var flag = StudentEmail.FlagReason(email, allowedDomains);
                                result.Status = flag != null ? ImportRowStatuses.Flagged : ImportRowStatuses.Ok;
                                result.Note = flag ?? (student.Email == null ? null : $"Replaces {student.Email}.");
                                changes.Add((student, email));
                            }
                        }
                    }
                    if (problem != null)
                    {
                        result.Status = ImportRowStatuses.Error;
                        result.Note = problem;
                        errors.Add($"Row {row}: {problem}");
                    }
                }

                var flagged = results.Count(r => r.Status == ImportRowStatuses.Flagged);
                var flaggedText = flagged > 0 ? $"; {flagged} flagged for checking" : "";
                if (errors.Count > 0)
                    return new StudentImportResult { Success = false, Message = $"Nothing was saved. Fix {errors.Count} problem(s) and upload again.", Errors = errors, Rows = results };
                if (changes.Count == 0)
                    return new StudentImportResult { Success = true, Message = "No email changes in this sheet.", Rows = results };
                if (dto.Preview)
                    return new StudentImportResult { Success = true, Message = $"{changes.Count} email(s) ready to save{flaggedText}. Check the list, then press Save.", Rows = results };

                using var transaction = await _context.Database.BeginTransactionAsync();
                foreach (var (student, email) in changes)
                {
                    var old = student.Email;
                    StudentEmail.Apply(student, email, allowedDomains);
                    _context.AuditLogs.Add(new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        Action = "UPDATE",
                        TableName = nameof(Models.StudentMaster),
                        RecordId = student.StdMstId.ToString(),
                        PerformedBy = _context.CurrentUserId,
                        UserType = "Staff",
                        Event = "EmailSet",
                        Details = $"Student {student.StudentId}: email {old ?? "(none)"} -> {email} from the email sheet."
                    });
                }
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return new StudentImportResult { Success = true, Saved = true, Message = $"{changes.Count} email(s) saved{flaggedText}.", Rows = results };
            }
        }

        public async Task<string> DeleteStudentAsync(string studentId)
        {
            var student = await _context.StudentMasters
                .FirstOrDefaultAsync(x => x.StudentId == studentId && !x.IsDeleted);

            if (student == null)
            {
                throw new InvalidOperationException("Student not found.");
            }
            student.IsDeleted = true;
            
            var eligibilities = await _context.StudentEligibilities
                .Where(x => x.StudentId == studentId && !x.IsDeleted)
                .ToListAsync();
            foreach (var item in eligibilities)
            {
                item.IsDeleted = true;
            }
            await _context.SaveChangesAsync();

            return "Student deleted successfully";

        }
        public async Task<List<ExamDetailsResultDto>> GetExamDetailsAsync(string studentId)
        {
            // IgnoreQueryFilters() is needed to see soft-deleted rows, but it also drops the
            // tenant filter -- so scope to the current college explicitly (StudentId is only
            // unique per college).
            var collegeId = _context.CurrentCollegeId;
            var data = await (
                from mm in _context.MarksMasters.IgnoreQueryFilters()
                join em in _context.Exams
                    on mm.ExamId equals em.ExamId
                where mm.StudentID == studentId && mm.CollegeId == collegeId
                select new
                {
                    mm.MarksId,
                    mm.StudentID,
                    em.ExamType,
                    Remark = mm.OverallRemark,
                    ExamName = em.Name,
                    mm.SemesterId,
                    mm.Pattern,
                    mm.IsDeleted,
                    mm.ExamId,
                    mm.UpdatedAt
                }
            ).ToListAsync();

            // Group by ExamId + SemesterId + StudentId to get latest update
            var result = data
                .GroupBy(x => new { x.StudentID, x.ExamId, x.SemesterId })
                .Select(g => g.OrderByDescending(x => x.UpdatedAt).First())
                .Select(x => new ExamDetailsResultDto
                {
                    StudentId = x.StudentID,
                    ExamType = x.ExamType,
                    Remark = x.Remark,
                    ExamName = x.ExamName,
                    SemesterId = x.SemesterId,
                    Pattern = x.Pattern,
                    ActionStatus = x.IsDeleted ? "Restore" : "Delete",
                    MarksId = x.MarksId.ToString()
                })
                .OrderBy(x => x.SemesterId)
                .ThenBy(x => x.ExamName)
                .ToList();

            return result;
        }

        public async Task<string> DeleteExamAsync(string studentId, Guid marksId)
        {
             
            var marksRecords = await _context.MarksMasters
                  .Where(x => x.StudentID == studentId && x.MarksId == marksId && !x.IsDeleted)
        .ToListAsync();

            // StudentMarks has no CollegeId of its own: it is scoped through its parent MarksMaster. Only touch the marks
            // when the (tenant-filtered) MarksMaster was found for this student in the caller's college, otherwise a
            // MarksId from another college would soft-delete that college's marks.
            if (!marksRecords.Any())
            {
                return "No records found to delete";
            }

            foreach (var record in marksRecords)
            {
                record.IsDeleted = true;
            }
            var studentRecord = await _context.StudentMarks
                .Where(x => x.MarksId == marksId && !x.IsDeleted)
        .ToListAsync();

            foreach (var record in studentRecord)
            {
                record.IsDeleted = true;
            }
        

            await _context.SaveChangesAsync();

            return "Deleted successfully";
        }

        public async Task<string> RestoreExamAsync(string studentId, Guid marksId)
        {
            // IgnoreQueryFilters() drops the tenant filter as well as the soft-delete one, so
            // scope to the current college explicitly. StudentMarks has no CollegeId of its own;
            // it is scoped through its parent MarksMaster.
            var collegeId = _context.CurrentCollegeId;
            var marksRecords = await _context.MarksMasters.IgnoreQueryFilters()
                .Where(x => x.StudentID == studentId && x.MarksId == marksId && x.CollegeId == collegeId)
                .ToListAsync();


            var studentRecords = await _context.StudentMarks.IgnoreQueryFilters()
                .Where(x => x.MarksId == marksId
                    && _context.MarksMasters.IgnoreQueryFilters()
                        .Any(m => m.MarksId == x.MarksId && m.CollegeId == collegeId))
                .ToListAsync();

            if (!marksRecords.Any() && !studentRecords.Any())
            {
                return "No deleted records found to restore";
            }

            foreach (var record in marksRecords)
            {
                record.IsDeleted = false;
            }

            foreach (var record in studentRecords)
            {
                record.IsDeleted = false;
            }

            await _context.SaveChangesAsync();

            return "Restore successfully";
        }
    }
}
 


