using ClosedXML.Excel;
using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.EMMA;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using System.Collections;
using System.Data;





namespace ExamAPI.Services.StudentMaster
{
    public class StudentMasterService : IStudentMasterService
    {

        private readonly ApplicationDbContext _context;


        public StudentMasterService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<StudentMasterDto>> GetDataAsync(Guid ayid)
        {
            var branches = await _context.CourseMasters
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.Name)
                .Select(c => new StudentMasterDto
                {
                    CourseId = c.CourseId,
                    Name = c.Name
                })
                .ToListAsync();

            return branches;
        }
        private async Task<string> GenerateStudentIdAsync(Guid courseId, Guid ayid)
        {
            // 1️⃣ Get Current Academic Year
            var academicYear = await _context.AcademicYears
                .Where(a => a.AYID == ayid)
                .Select(a => new { a.AYID, a.ShortDuration })
                .FirstOrDefaultAsync();

            if (academicYear == null)
                throw new Exception("No Data Not Found");

            // Example: ShortDuration = "2024-25"
            string yearPart = academicYear.ShortDuration.Substring(2, 2);

            // 2️⃣ Get Course Code
            var courseCode = await _context.CourseMasters
                .Where(c => c.CourseId == courseId)
                .Select(c => c.CourseCode)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(courseCode))
                throw new Exception("Invalid Course selected.");

            string coursePart = courseCode.Substring(courseCode.Length - 2);

            // 3️⃣ Get Last StudentId for this Course
            var lastStudentId = await _context.StudentEligibilities
                .Where(e => e.CourseId == courseId && e.AYID == ayid)
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => e.StudentId)
                .FirstOrDefaultAsync();

            int nextNumber = 1;

            if (!string.IsNullOrEmpty(lastStudentId) && lastStudentId.Length >= 8)
            {
                string numberPart = lastStudentId.Substring(4);
                nextNumber = int.Parse(numberPart) + 1;
            }

            return $"{yearPart}{coursePart}{nextNumber:D4}";
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
                    Dyslexia = sm.Dyslexia
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
                query = query.Where(x => x.sm.MiddleName.StartsWith(model.MiddleName));

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
                    AYID = (Guid)x.se.AYID
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

            string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");

            // PHOTO UPDATE
            if (!string.IsNullOrWhiteSpace(dto.PhotoUrl))
            {
                if (dto.PhotoUrl.StartsWith("data:image"))
                {
                    Guid imageGuid = Guid.NewGuid();
                    string photoFileName = $"{imageGuid}_photo.png";

                    student.PhotoUrl = SaveBase64Image(dto.PhotoUrl, uploadsFolder, photoFileName);
                }
            }

            if (!string.IsNullOrWhiteSpace(dto.SignUrl))
            {
                if (dto.SignUrl.StartsWith("data:image"))
                {
                    Guid imageGuid = Guid.NewGuid();
                    string signFileName = $"{imageGuid}_sign.png";

                    student.SignUrl = SaveBase64Image(dto.SignUrl, uploadsFolder, signFileName);
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
                    
                    Gender = dto.Gender,
                    Dyslexia = dto.Dyslexia,
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                };

                // ✅ HANDLE IMAGE AFTER OBJECT CREATION
                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");

                if (!string.IsNullOrWhiteSpace(dto.PhotoUrl) && dto.PhotoUrl.StartsWith("data:image"))
                {
                    Guid imageGuid = Guid.NewGuid();
                    string photoFileName = $"{imageGuid}_photo.png";

                    studentMaster.PhotoUrl = SaveBase64Image(dto.PhotoUrl, uploadsFolder, photoFileName);
                }

                if (!string.IsNullOrWhiteSpace(dto.SignUrl) && dto.SignUrl.StartsWith("data:image"))
                {
                    Guid imageGuid = Guid.NewGuid();
                    string signFileName = $"{imageGuid}_sign.png";

                    studentMaster.SignUrl = SaveBase64Image(dto.SignUrl, uploadsFolder, signFileName);
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
        private string? SaveBase64Image(string? base64Data, string folderPath, string fileName)
        {
            if (string.IsNullOrEmpty(base64Data))
                return null;

            var commaIndex = base64Data.IndexOf(',');
            if (commaIndex >= 0)
                base64Data = base64Data.Substring(commaIndex + 1);

            byte[] imageBytes = Convert.FromBase64String(base64Data);

            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            string filePath = Path.Combine(folderPath, fileName);
            File.WriteAllBytes(filePath, imageBytes);

            return $"/uploads/{fileName}";
        }
        public async Task<(byte[] FileBytes, string FileName)> GenerateExcelTemplateAsync(StudExcelDto dto)
        {
            using var workbook = new XLWorkbook();

            // 🔹 Get Course Name
            var course = await _context.CourseMasters
                .Where(c => c.CourseId == dto.CourseId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync();

            // 🔹 Get Academic Year
            var academicYear = await _context.AcademicYears
                .Where(a => a.AYID == dto.AYID)
                .Select(a => a.ShortDuration)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(course) || string.IsNullOrEmpty(academicYear))
                throw new Exception("Invalid data");

            var worksheet = workbook.Worksheets.Add("StudentTemplate");

            // ================= HEADER LINE =================
            string headerText = $"{course}   {dto.SemesterId}  {dto.Pattern}  {academicYear}";
            worksheet.Cell(1, 1).Value = headerText;
            worksheet.Range("A1:F1").Merge();
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontSize = 14;
            worksheet.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // ================= COLUMN HEADERS =================
            string[] headers = { "FirstName", "MiddleName", "LastName", "Category", "PRN", "Gender" };
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(2, i + 1).Value = headers[i];
                worksheet.Cell(2, i + 1).Style.Font.Bold = true;
            }

            // ================= GENDER VALIDATION =================

            var genderRange = worksheet.Range("F3:F1000");

            genderRange.AddConditionalFormat()
                .WhenIsTrue(@"=AND(F3<>"""", NOT(OR(
        LOWER(F3)=""male"",
        LOWER(F3)=""female""
    )))")
                .Fill.SetBackgroundColor(XLColor.LightSkyBlue);

            // ================= PRN VALIDATION =================

            var prnRange = worksheet.Range("E3:E1000");

            // Highlight duplicates **only in PRN column**
            prnRange.AddConditionalFormat()
                    .WhenIsDuplicate()
                    .Fill.SetBackgroundColor(XLColor.LightPink);

            prnRange.AddConditionalFormat()
                   .WhenIsTrue(@"=AND(E3<>"""",NOT(ISNUMBER(E3)))")
                   .Fill.SetBackgroundColor(XLColor.LightGreen);
            // Auto adjust columns
            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            string fileName = $"{dto.CourseId}_{dto.SemesterId}_{dto.Pattern}_{academicYear}.xlsx";

            return (stream.ToArray(), fileName);
        }
        public async Task<object> ImportStudentsAsync(StudentImportDto dto)
        {
            var errors = new List<string>();

            var academicYear = await _context.AcademicYears
            .Where(a => a.AYID == dto.AYID)
            .Select(a => new { a.AYID, a.ShortDuration })
            .FirstOrDefaultAsync();

            if (academicYear == null)
                throw new Exception("Current Academic Year not found.");

            // ✅ 2. Load Excel
            using var stream = new MemoryStream();
            await dto.File.CopyToAsync(stream);

            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheet(1);

            // ✅ 3. Validate header
            var headerRow = sheet.Row(2);
            string[] expectedHeaders = { "FirstName", "MiddleName", "LastName", "Category", "PRN", "Gender" };

            for (int i = 0; i < expectedHeaders.Length; i++)
            {
                var actual = headerRow.Cell(i + 1).GetValue<string>()?.Trim() ?? "";
                if (!string.Equals(actual, expectedHeaders[i], StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception("Invalid Excel format. Please download latest template.");
                }
            }

            int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            if (lastRow < 3)
                throw new Exception("Excel must contain at least 1 data row.");

            var existingPRNs = await _context.StudentMasters
                .Where(s => !s.IsDeleted)
                .Select(s => s.StudentPRN)
                .ToHashSetAsync();

            var newPRNsInFile = new HashSet<string>();

            var baseStudentId = await GenerateStudentIdAsync(dto.CourseId, academicYear.AYID);
            var prefix = new string(baseStudentId.TakeWhile(c => !char.IsDigit(c)).ToArray());
            var numberPart = int.Parse(new string(baseStudentId.SkipWhile(c => !char.IsDigit(c)).ToArray()));

            var rows = sheet.RowsUsed().Skip(2).ToList(); // data rows only

            // ✅ 4. VALIDATION PHASE: collect all errors first
            foreach (var row in rows)
            {
                string firstName = row.Cell(1).GetValue<string>()?.Trim() ?? "";
                string middleName = row.Cell(2).GetValue<string>()?.Trim() ?? "";
                string lastName = row.Cell(3).GetValue<string>()?.Trim() ?? "";
                string category = row.Cell(4).GetValue<string>()?.Trim() ?? "";
                string prn = row.Cell(5).GetValue<string>()?.Trim() ?? "";
                string gender = row.Cell(6).GetValue<string>()?.Trim();

                // Skip completely empty row
                if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
                    continue;

                // Mandatory fields
                if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
                    errors.Add($"Row {row.RowNumber()}: FirstName or LastName missing");

                // PRN duplicates
                if (!string.IsNullOrEmpty(prn))
                {
                    if (existingPRNs.Contains(prn))
                        errors.Add($"Row {row.RowNumber()}: PRN '{prn}' already exists in database");

                    newPRNsInFile.Add(prn);
                }
            }

            // ❌ IF ANY ERRORS, ABORT ENTIRE IMPORT
            if (errors.Count > 0)
            {
                return new
                {
                    Success = false,
                    Errors = errors
                };
            }

            // ✅ 5. IMPORT PHASE: only if validation passed
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var row in rows)
                {
                    string firstName = row.Cell(1).GetValue<string>()?.Trim() ?? "";
                    string middleName = row.Cell(2).GetValue<string>()?.Trim() ?? "";
                    string lastName = row.Cell(3).GetValue<string>()?.Trim() ?? "";
                    string category = row.Cell(4).GetValue<string>()?.Trim() ?? "";
                    string prn = row.Cell(5).GetValue<string>()?.Trim() ?? "";
                    string gender = row.Cell(6).GetValue<string>()?.Trim();

                    // Skip completely empty row
                    if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
                        continue;

                    var studentId = $"{prefix}{numberPart:D4}";
                    numberPart++;

                    var stdMstId = Guid.NewGuid();

                    // StudentMaster
                    var studentMaster = new Models.StudentMaster
                    {
                        StdMstId = stdMstId,
                        StudentId = studentId,
                        FirstName = firstName,
                        MiddleName = middleName,
                        LastName = lastName,
                        Category = category,
                        StudentPRN = prn,
                        Gender = string.IsNullOrEmpty(gender) ? null : gender,
                        Dyslexia = false,
                        CreatedAt = DateTime.UtcNow,
                        IsDeleted = false
                    };
                    _context.StudentMasters.Add(studentMaster);

                    // StudentEligibility
                    var eligibility = new StudentEligibility
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
                    };
                    _context.StudentEligibilities.Add(eligibility);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new { Success = true, Message = "Import successful" };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
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
            var data = await (
                from mm in _context.MarksMasters.IgnoreQueryFilters()
                join em in _context.Exams
                    on mm.ExamId equals em.ExamId
                where mm.StudentID == studentId
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
            var marksRecords = await _context.MarksMasters.IgnoreQueryFilters()
                .Where(x => x.StudentID == studentId && x.MarksId == marksId )
                .ToListAsync();

            
            var studentRecords = await _context.StudentMarks.IgnoreQueryFilters()
                .Where(x => x.MarksId == marksId )
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
 


