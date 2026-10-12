using ClosedXML.Excel;
using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.EMMA;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Data;





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
        /// Student ID = 2-digit year + 2-character branch tag + running number, e.g. 26IL0001
        /// (year from the academic year "2026-27", tag = last two letters/digits of the branch code "CIVIL").
        /// The number continues from the highest ID with the same year + tag in this college -- deleted
        /// students included, since (CollegeId, StudentId) is unique -- so two branches whose codes end
        /// alike share one counter instead of handing out the same ID.
        /// </summary>
        private async Task<(string Prefix, int Next)> NextStudentIdAsync(Guid courseId, Guid ayid)
        {
            var shortDuration = await _context.AcademicYears
                .Where(a => a.AYID == ayid)
                .Select(a => a.ShortDuration)
                .FirstOrDefaultAsync();
            var yearDigits = new string((shortDuration ?? "").TakeWhile(char.IsDigit).ToArray());
            if (yearDigits.Length < 2)
                throw new InvalidOperationException("The academic year has no year in its label (e.g. 2026-27).");
            var yearPart = yearDigits.Length >= 4 ? yearDigits.Substring(2, 2) : yearDigits.Substring(0, 2);

            var courseCode = await _context.CourseMasters
                .Where(c => c.CourseId == courseId)
                .Select(c => c.CourseCode)
                .FirstOrDefaultAsync();
            if (string.IsNullOrEmpty(courseCode))
                throw new InvalidOperationException("Select a branch.");
            var clean = new string(courseCode.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            var tag = clean.Length >= 2 ? clean[^2..] : clean.PadRight(2, 'X');

            var prefix = yearPart + tag;
            var collegeId = _context.CurrentCollegeId;
            var existing = await _context.StudentMasters
                .IgnoreQueryFilters()
                .Where(s => s.CollegeId == collegeId && s.StudentId.StartsWith(prefix))
                .Select(s => s.StudentId)
                .ToListAsync();

            var highest = existing
                .Select(id => int.TryParse(id[prefix.Length..], out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max();
            return (prefix, highest + 1);
        }

        private async Task<string> GenerateStudentIdAsync(Guid courseId, Guid ayid)
        {
            var (prefix, next) = await NextStudentIdAsync(courseId, ayid);
            return $"{prefix}{next:D4}";
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
                    StudentName = sm.FirstName + " " + (sm.MiddleName ?? "") + " " + sm.LastName,
                    SemesterId = se.SemesterId,
                    StudentPRN = sm.StudentPRN,
                    Dyslexia = sm.Dyslexia,
                    PhotoUrl = sm.PhotoUrl,
                    SignUrl = sm.SignUrl
                }
            ).ToListAsync();

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
                    StudentName = x.sm.FirstName + " " + (x.sm.MiddleName ?? "") + " " + x.sm.LastName,
                    SemesterId = x.se.SemesterId,
                    Pattern = x.se.Pattern,
                    StudentPRN = x.sm.StudentPRN,
                    AYID = x.se.AYID ?? Guid.Empty,
                    PhotoUrl = x.sm.PhotoUrl,
                    SignUrl = x.sm.SignUrl
                })
                .ToListAsync();

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
                    Gender = sm.Gender,
                    Category = sm.Category,
                    StudentPRN = sm.StudentPRN,
                    PhotoUrl = sm.PhotoUrl,
                    SignUrl = sm.SignUrl,
                    Dyslexia = sm.Dyslexia
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
                throw new Exception("Student not found");
            var pnrExists = await _context.StudentMasters
                .AnyAsync(x => x.StudentPRN == dto.StudentPRN && x.StudentId != dto.StudentId && !x.IsDeleted);

            if (pnrExists)
            {
                throw new Exception(" PNR already exists. ");
            }
            // 2️⃣ Update basic fields

            student.FirstName = dto.FirstName;
            student.MiddleName = dto.MiddleName;
            student.LastName = dto.LastName;
            student.Category = dto.Category;
            student.StudentPRN = dto.StudentPRN;
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
            // 5️⃣ Update Eligibility
            var eligibility = await _context.StudentEligibilities
                .FirstOrDefaultAsync(x => x.StudentId == dto.StudentId && !x.IsDeleted);

            if (eligibility != null)
            {
                eligibility.CourseId = dto.CourseId;
                eligibility.SemesterId = dto.SemesterId;
                eligibility.Pattern = dto.Pattern;
            }

            await _context.SaveChangesAsync();

            return "Student updated successfully";
        }
        public async Task<string> SaveStudentAsync(Savedata dto)
        {
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
                string studentId = await GenerateStudentIdAsync(dto.CourseId, dto.AYID);

                var pnrExists = await _context.StudentMasters
                    .AnyAsync(x => x.StudentPRN == dto.StudentPRN && x.StudentId != dto.StudentId && !x.IsDeleted);

                if (pnrExists)
                    throw new InvalidOperationException("PRN already exists");


                var studentMaster = new Models.StudentMaster
                {
                    StdMstId = stdMstId,
                    StudentId = studentId,
                    FirstName = dto.FirstName,
                    MiddleName = dto.MiddleName,
                    LastName = dto.LastName,
                    Category = dto.Category,
                    StudentPRN=dto.StudentPRN,
                    Gender = dto.Gender,
                    Dyslexia = dto.Dyslexia,
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                };

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
        private static readonly string[] TemplateHeaders = { "FirstName", "MiddleName", "LastName", "Category", "PRN", "Gender" };
        private const int TemplateRows = 1000;

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
            data.Style.NumberFormat.Format = "@"; // text, so long PRNs are not turned into 1.23E+14

            // Gender: a drop-down with the two values the screen uses.
            var gender = sheet.Range(3, 6, TemplateRows + 2, 6).CreateDataValidation();
            gender.List("\"Male,Female\"", true);
            gender.ErrorTitle = "Gender";
            gender.ErrorMessage = "Choose Male or Female.";

            // PRN: highlight a number typed twice in this sheet.
            sheet.Range(3, 5, TemplateRows + 2, 5).AddConditionalFormat().WhenIsDuplicate().Fill.SetBackgroundColor(XLColor.LightPink);

            double[] widths = { 18, 18, 18, 14, 20, 10 };
            for (int i = 0; i < widths.Length; i++) sheet.Column(i + 1).Width = widths[i];

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            // The controller adds ".xlsx".
            var fileName = string.Concat($"Students {course} {dto.SemesterId} {dto.Pattern} {academicYear}"
                .Split(Path.GetInvalidFileNameChars()));
            return (stream.ToArray(), fileName);
        }

        public async Task<object> ImportStudentsAsync(StudentImportDto dto)
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
                for (int i = 0; i < TemplateHeaders.Length; i++)
                {
                    var actual = sheet.Cell(2, i + 1).GetString().Trim();
                    if (!string.Equals(actual, TemplateHeaders[i], StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("This is not the student template. Download it again with the Excel button.");
                }

                // Read every filled row first; nothing is saved unless all rows are valid.
                var rows = new List<(int Row, string First, string Middle, string Last, string Category, string Prn, string Gender)>();
                int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
                for (int r = 3; r <= lastRow; r++)
                {
                    string Cell(int c) => sheet.Cell(r, c).GetFormattedString().Trim();
                    var row = (r, Cell(1), Cell(2), Cell(3), Cell(4), Cell(5), Cell(6));
                    if (row.Item2 == "" && row.Item3 == "" && row.Item4 == "" && row.Item5 == "" && row.Item6 == "" && row.Item7 == "")
                        continue; // empty row
                    rows.Add(row);
                }
                if (rows.Count == 0)
                    throw new InvalidOperationException("The file has no students. Fill them in from row 3.");

                var existingPrns = await _context.StudentMasters
                    .Where(s => !s.IsDeleted && s.StudentPRN != null && s.StudentPRN != "")
                    .Select(s => s.StudentPRN!)
                    .ToHashSetAsync();
                var prnsInFile = new HashSet<string>();
                var errors = new List<string>();

                foreach (var (row, first, middle, last, category, prn, gender) in rows)
                {
                    if (first == "" || last == "")
                        errors.Add($"Row {row}: first name and last name are required.");
                    if (first.Length > 50 || middle.Length > 50 || last.Length > 50 || category.Length > 50 || prn.Length > 50)
                        errors.Add($"Row {row}: a value is longer than 50 characters.");
                    if (!string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase) && !string.Equals(gender, "Female", StringComparison.OrdinalIgnoreCase))
                        errors.Add($"Row {row}: gender must be Male or Female.");
                    if (prn != "")
                    {
                        if (existingPrns.Contains(prn))
                            errors.Add($"Row {row}: PRN {prn} already belongs to a student.");
                        else if (!prnsInFile.Add(prn))
                            errors.Add($"Row {row}: PRN {prn} appears twice in this file.");
                    }
                }

                if (errors.Count > 0)
                    return new { Success = false, Message = $"Nothing was imported. Fix {errors.Count} problem(s) and import again.", Errors = errors };

                var (prefix, numberPart) = await NextStudentIdAsync(dto.CourseId, academicYear.AYID);

                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    foreach (var (_, first, middle, last, category, prn, gender) in rows)
                    {
                        var studentId = $"{prefix}{numberPart:D4}";
                        numberPart++;
                        var stdMstId = Guid.NewGuid();

                        _context.StudentMasters.Add(new Models.StudentMaster
                        {
                            StdMstId = stdMstId,
                            StudentId = studentId,
                            FirstName = first,
                            MiddleName = middle == "" ? null : middle,
                            LastName = last,
                            Category = category == "" ? null : category,
                            StudentPRN = prn == "" ? null : prn,
                            Gender = string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase) ? "Male" : "Female",
                            Dyslexia = false,
                            CreatedAt = DateTime.UtcNow,
                            IsDeleted = false
                        });
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
                    return new { Success = true, Message = $"{rows.Count} student(s) imported.", Errors = new List<string>() };
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
        }
        public async Task<string> DeleteStudentAsync(string studentId)
        {
            var student = await _context.StudentMasters
                .FirstOrDefaultAsync(x => x.StudentId == studentId && !x.IsDeleted);

            if (student == null)
            {
                throw new Exception("Student not Found");
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
 


