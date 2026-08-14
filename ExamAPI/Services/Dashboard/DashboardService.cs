using ExamAPI.Services.Dashboard;
using ExamAPI.DTOs;
using ExamAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Dashboard
{
    public class DashboardService : IDashboardService
    {
        private readonly ApplicationDbContext _context;

        public DashboardService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<CourseStudentCountDTO>> GetCourseStudentCountAsync(Guid ayId)
        {
            var result = await (
                from se in _context.StudentEligibilities
                join cm in _context.CourseMasters
                    on se.CourseId equals cm.CourseId
                where !se.IsDeleted
                      && !cm.IsDeleted
                      && se.AYID == ayId
                group se by new { cm.CourseId, cm.Name } into g
                select new CourseStudentCountDTO
                {
                    CourseId = g.Key.CourseId,
                    CourseName = g.Key.Name,
                    StudentCount = g.Count()
                }
            ).ToListAsync();

            return result;
        }


        public async Task<List<SemesterStudentCountDTO>> GetSemesterWiseStudentCountAsync(Guid courseId, Guid ayId)
        {
            var eligibleStudentIds = _context.StudentEligibilities.Where(se => se.CourseId == courseId && !se.IsDeleted).Select(se => se.StdMstId);

            return await _context.MarksMasters.Where(mm=> eligibleStudentIds.Contains(mm.StdMstId) && mm.AcademicYearAYID == ayId && !mm.IsDeleted).GroupBy(mm=>mm.SemesterId).Select(g=>new SemesterStudentCountDTO
            {
                SemesterId=g.Key,
                StudentCount=g.Select(x=>x.StdMstId).Distinct().Count(),
            }).ToListAsync();
        }

        public async Task<List<PassFailChartDTO>> GetPassFailChartAsync(Guid courseId, Guid ayId)
        {

            var debugData = await (
    from mm in _context.MarksMasters
    join se in _context.StudentEligibilities
        on mm.StdMstId equals se.StdMstId
    where !se.IsDeleted
          && !mm.IsDeleted
          && mm.AcademicYearAYID == se.AYID
          && se.CourseId == courseId
          && mm.AcademicYearAYID == ayId
    select new
    {
        mm.SemesterId,
        mm.OverallRemark
    }
).ToListAsync();

            var result = await (
                from mm in _context.MarksMasters
                join se in _context.StudentEligibilities
                    on mm.StdMstId equals se.StdMstId
                where !se.IsDeleted
                      && !mm.IsDeleted
                      && mm.AcademicYearAYID == se.AYID
                      && se.CourseId == courseId
                      && mm.AcademicYearAYID == ayId
                group mm by mm.SemesterId into g
                select new PassFailChartDTO
                {
                    SemesterId = g.Key,

                    PassCount = g.Count(x =>
                        x.OverallRemark != null &&
                        x.OverallRemark.Trim().ToLower() == "successful"),

                    FailCount = g.Count(x =>
                        x.OverallRemark != null &&
                        x.OverallRemark.Trim().ToLower() == "unsuccessful")
                }
            ).ToListAsync();

            return result;
        }

        public async Task<List<SemesterExamTypeCountDTO>> GetSemesterWiseExamTypeCountAsync(Guid courseId, Guid ayId)
        {
            var result = await (
                from mm in _context.MarksMasters
                join em in _context.Exams
                    on mm.ExamId equals em.ExamId
                join se in _context.StudentEligibilities
                    on new { em.Semester, em.AcademicYearAYID }
                    equals new { Semester = se.SemesterId, AcademicYearAYID = se.AYID }
                where !mm.IsDeleted
                      && !em.IsDeleted
                      && !se.IsDeleted
                      && mm.AcademicYearAYID == em.AcademicYearAYID
                      && se.CourseId == courseId
                      && mm.AcademicYearAYID == ayId
                group se by new { mm.SemesterId, em.ExamType } into g
                select new SemesterExamTypeCountDTO
                {
                    SemesterId = g.Key.SemesterId,
                    ExamType = g.Key.ExamType,
                    StudentCount = g.Select(x => x.StdMstId).Distinct().Count()
                }
            ).ToListAsync();

            return result;
        }

        //public async Task<List<PassFailChartDTO>> GetPassFailChartAsync(Guid courseId, Guid ayId)
        //{
        //    var result = await (
        //        from mm in _context.MarksMasters
        //        join se in _context.StudentEligibilities
        //            on mm.StdMstId equals se.StdMstId
        //        where !se.IsDeleted
        //              && !mm.IsDeleted
        //              && mm.AcademicYearAYID == se.AYID
        //              && se.CourseId == courseId
        //              && mm.AcademicYearAYID == ayId
        //        group mm by mm.SemesterId into g
        //        select new PassFailChartDTO
        //        {
        //            SemesterId = g.Key,
        //            PassCount = g.Count(x => x.OverallRemark == "Pass"),
        //            FailCount = g.Count(x => x.OverallRemark == "Fail")
        //        }
        //    ).ToListAsync();

        //    return result;
        //}

    }
}
