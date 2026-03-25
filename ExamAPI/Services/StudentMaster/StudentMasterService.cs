<<<<<<< HEAD
﻿using ClosedXML.Excel;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;

=======
﻿using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; 
>>>>>>> 49b1e581466adc2308420e40d667e717b5a343fa

namespace ExamAPI.Services.StudentMaster
{
    public class StudentMasterService : IStudentMasterService
    {
<<<<<<< HEAD

=======
>>>>>>> 49b1e581466adc2308420e40d667e717b5a343fa
        private readonly ApplicationDbContext _context;

        public StudentMasterService(ApplicationDbContext context)
        {
            _context = context;
        }
<<<<<<< HEAD

=======
         
>>>>>>> 49b1e581466adc2308420e40d667e717b5a343fa
        public async Task<List<StudentMasterDto>> GetDataAsync()
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
<<<<<<< HEAD
=======

        private async Task<string> GenerateStudentIdAsync(Guid courseId)
        {
            // 1️⃣ Get Current Academic Year
            var academicYear = await _context.AcademicYears
                .Where(a => a.IsCurrent)
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
                .Where(e => e.CourseId == courseId)
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

        public async Task<string> SaveStudentAsync(Savedata dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1️⃣ Get Current Academic Year (Real AYID)
                var academicYear = await _context.AcademicYears
                    .Where(a => a.IsCurrent)
                    .Select(a => new { a.AYID, a.ShortDuration })
                    .FirstOrDefaultAsync();

                if (academicYear == null)
                    throw new Exception("No current academic year found.");

                Guid stdMstId = Guid.NewGuid();

                // 2️⃣ Generate StudentId
                string studentId = await GenerateStudentIdAsync(dto.CourseId);

                // 3️⃣ Save StudentMaster
                var studentMaster = new Models.StudentMaster
                {
                    StdMstId = stdMstId,
                    StudentId = studentId,  
                    FirstName = dto.FirstName,
                    MiddleName = dto.MiddleName,
                    LastName = dto.LastName,
                    Category = dto.Category,
                    StudentPRN = dto.StudentPRN,
                    Gender = dto.Gender,
                    Dyslexia = dto.Dyslexia,

                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                };

                _context.StudentMasters.Add(studentMaster);



                var Id = Guid.NewGuid();
                // 4️⃣ Save StudentEligibility
                var studentEligibility = new StudentEligibility
                {
                    Id= Id,
                    StdMstId = stdMstId,
                    StudentId = studentId,
                    CourseId = dto.CourseId,
                    SemesterId = dto.SemesterId,
                    AYID = academicYear.ShortDuration,
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                };

                _context.StudentEligibilities.Add(studentEligibility);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return studentId; // ✅ Return to frontend
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

>>>>>>> 49b1e581466adc2308420e40d667e717b5a343fa
        public async Task<List<FetchData>> GetbycourseAsync(Guid courseId)
        {
            // Step 1: Get Current Academic Year (ShortDuration)
            var currentAY = await _context.AcademicYears
                .Where(a => a.IsCurrent == true)
                .Select(a => a.ShortDuration)
                .FirstOrDefaultAsync();
<<<<<<< HEAD

=======
 
>>>>>>> 49b1e581466adc2308420e40d667e717b5a343fa
            var data = await (
                from se in _context.StudentEligibilities
                join sm in _context.StudentMasters on se.StudentId equals sm.StudentId
                join cm in _context.CourseMasters on se.CourseId equals cm.CourseId
                where se.CourseId == courseId
                      && se.AYID == currentAY
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
                    StudentPRN = sm.StudentPRN
                }
            ).ToListAsync();

            return data;
        }
<<<<<<< HEAD
        private async Task<string> GenerateStudentIdAsync(Guid courseId)
        {
            // 1️⃣ Get Current Academic Year
            var academicYear = await _context.AcademicYears
                .Where(a => a.IsCurrent)
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

            var lastStudentId = await _context.StudentEligibilities
     .IgnoreQueryFilters()
     .Where(e => e.CourseId == courseId)
     .OrderByDescending(e => e.StudentId)
     .Select(e => e.StudentId)
     .FirstOrDefaultAsync();
                
            int nextNumber = 1;

            if (!string.IsNullOrEmpty(lastStudentId) &&
                int.TryParse(lastStudentId.Substring(4), out int last))
            {
                nextNumber = last + 1;
            }

            return $"{yearPart}{coursePart}{nextNumber:D4}";
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
        public async Task<string> SaveStudentAsync(Savedata dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var academicYear = await _context.AcademicYears
                    .Where(a => a.IsCurrent)
                    .Select(a => new { a.AYID, a.ShortDuration })
                    .FirstOrDefaultAsync();

                if (academicYear == null)
                    throw new Exception("No current academic year found.");

                Guid stdMstId = Guid.NewGuid();
                Guid imageGuid = Guid.NewGuid();
                string studentId = await GenerateStudentIdAsync(dto.CourseId);

                // 1️⃣ Save images to server
                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");

                string? photoUrl = null;
                string? signUrl = null;

                if (!string.IsNullOrEmpty(dto.PhotoUrl))
                {
                    Guid photoGuid = Guid.NewGuid();
                    string photoFileName = $"{photoGuid}_photo.png";
                    photoUrl = SaveBase64Image(dto.PhotoUrl, uploadsFolder, photoFileName);
                }

                if (!string.IsNullOrEmpty(dto.SignUrl))
                {
                    Guid signGuid = Guid.NewGuid();
                    string signFileName = $"{signGuid}_sign.png";
                    signUrl = SaveBase64Image(dto.SignUrl, uploadsFolder, signFileName);
                }

                // 2️⃣ Save StudentMaster
                var studentMaster = new Models.StudentMaster
                {
                    StdMstId = stdMstId,
                    StudentId = studentId,
                    FirstName = dto.FirstName,
                    MiddleName = dto.MiddleName,
                    LastName = dto.LastName,
                    Category = dto.Category,
                    StudentPRN = dto.StudentPRN,
                    Gender = dto.Gender,
                    PhotoUrl = photoUrl,
                    SignUrl = signUrl,
                    Dyslexia = dto.Dyslexia,
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                };

                var pnrExists = await _context.StudentMasters
                    .AnyAsync(x => x.StudentPRN == dto.StudentPRN && !x.IsDeleted);

                _context.StudentMasters.Add(studentMaster);

                // 3️⃣ Save StudentEligibility
                var studentEligibility = new StudentEligibility
                {
                    Id = Guid.NewGuid(),
                    StdMstId = stdMstId,
                    StudentId = studentId,
                    CourseId = dto.CourseId,
                    SemesterId = dto.SemesterId,
                    AYID = academicYear.ShortDuration,
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

=======
>>>>>>> 49b1e581466adc2308420e40d667e717b5a343fa
        public async Task<List<FetchData>> SearchStudentsAsync(Searchbyname model)
        {
            var currentAY = await _context.AcademicYears
                .Where(a => a.IsCurrent == true)
                .Select(a => a.ShortDuration)
                .FirstOrDefaultAsync();

            var query = from se in _context.StudentEligibilities
                        join sm in _context.StudentMasters on se.StudentId equals sm.StudentId
                        join cm in _context.CourseMasters on se.CourseId equals cm.CourseId
                        where se.AYID == currentAY
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
                    StudentPRN = x.sm.StudentPRN
                })
                .ToListAsync();

            return data;
        }
<<<<<<< HEAD
        public async Task<Savedata> GetStudentByIdAsync(string studentId)
        {
            var data = await (
                from sm in _context.StudentMasters
                join se in _context.StudentEligibilities
                    on sm.StudentId equals se.StudentId
                where sm.StudentId == studentId && !sm.IsDeleted
                select new Savedata
                {
                    StudentId = sm.StudentId,
                    CourseId = se.CourseId.Value,
                    SemesterId = se.SemesterId,
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
            }

            await _context.SaveChangesAsync();

            return "Student updated successfully";
        }
        public async Task<(byte[] FileBytes, string FileName)> GenerateExcelTemplateAsync(Guid courseId, int semesterId)
        {
            using var workbook = new XLWorkbook();

            // 🔹 Get Course Name
            var course = await _context.CourseMasters
                .Where(c => c.CourseId == courseId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync();

            // 🔹 Get Academic Year
            var academicYear = await _context.AcademicYears
                .Where(a => a.IsCurrent)
                .Select(a => a.ShortDuration)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(course) || string.IsNullOrEmpty(academicYear))
                throw new Exception("Invalid data");

            var worksheet = workbook.Worksheets.Add("StudentTemplate");

            // ================= HEADER LINE =================
            string headerText = $"{course}    Sem{semesterId}  {academicYear}";
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

            // ================= PRN VALIDATION =================
            var prnRange = worksheet.Range("E3:E1000");

            // Highlight duplicates **only in PRN column**
            prnRange.AddConditionalFormat()
                    .WhenIsDuplicate()
                    .Fill.SetBackgroundColor(XLColor.LightPink);

            // Auto adjust columns
            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            string fileName = $"{course}_Sem{semesterId}_{academicYear}.xlsx";

            return (stream.ToArray(), fileName);
        }

        public async Task<object> ImportStudentsAsync(StudentImportDto dto)
        {
            var errors = new List<string>();

            var academicYear = await _context.AcademicYears
                .Where(a => a.IsCurrent)
                .Select(a => a.ShortDuration)
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

            var baseStudentId = await GenerateStudentIdAsync(dto.CourseId);
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
                        SemesterId = dto.SemesterId,
                        AYID = academicYear,
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
    }
}
=======
    }
}
 
>>>>>>> 49b1e581466adc2308420e40d667e717b5a343fa
