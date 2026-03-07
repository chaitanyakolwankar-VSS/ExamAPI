using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; 

namespace ExamAPI.Services.StudentMaster
{
    public class StudentMasterService : IStudentMasterService
    {
        private readonly ApplicationDbContext _context;

        public StudentMasterService(ApplicationDbContext context)
        {
            _context = context;
        }
         
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

        public async Task<List<FetchData>> GetbycourseAsync(Guid courseId)
        {
            // Step 1: Get Current Academic Year (ShortDuration)
            var currentAY = await _context.AcademicYears
                .Where(a => a.IsCurrent == true)
                .Select(a => a.ShortDuration)
                .FirstOrDefaultAsync();
 
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
    }
}
 