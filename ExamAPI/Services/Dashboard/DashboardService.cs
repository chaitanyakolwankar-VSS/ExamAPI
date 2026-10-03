using DocumentFormat.OpenXml.InkML;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Services.Dashboard;
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

            return await _context.MarksMasters.Where(mm => eligibleStudentIds.Contains(mm.StdMstId) && mm.AcademicYearAYID == ayId && !mm.IsDeleted).GroupBy(mm => mm.SemesterId).Select(g => new SemesterStudentCountDTO
            {
                SemesterId = g.Key,
                StudentCount = g.Select(x => x.StdMstId).Distinct().Count(),
            }).ToListAsync();
        }

        public async Task<List<PassFailChartDTO>> GetPassFailChartAsync(Guid courseId, Guid ayId)
        {
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
                        x.OverallRemark.Trim().ToLower() == "pass"),

                    FailCount = g.Count(x =>
                        x.OverallRemark != null &&
                        x.OverallRemark.Trim().ToLower() == "fail")
                }
            ).ToListAsync();

            return result;
        }

        public async Task<List<SemesterExamTypeCountDTO>> GetSemesterWiseExamTypeCountAsync(Guid courseId, Guid ayId)
        {
            // ExamMaster.Semester is never written, so the semester comes from the students' MarksMaster rows
            // (MarksMaster.SemesterId). Tenant isolation: every set here sits behind the global college filter.
            var result = await (
                from mm in _context.MarksMasters
                join em in _context.Exams
                    on mm.ExamId equals em.ExamId
                where !mm.IsDeleted
                      && !em.IsDeleted
                      && em.CourseId == courseId
                      && mm.AcademicYearAYID == ayId
                      && mm.AcademicYearAYID == em.AcademicYearAYID
                group mm by new { mm.SemesterId, em.ExamType } into g
                select new SemesterExamTypeCountDTO
                {
                    SemesterId = g.Key.SemesterId,
                    ExamType = g.Key.ExamType,
                    StudentCount = g.Select(x => x.StdMstId).Distinct().Count()
                }
            ).ToListAsync();

            return result;
        }

        public async Task<DashboardStatsDTO> GetDashboardStatsAsync(Guid collegeId, Guid ayId)
        {
            var stats = new DashboardStatsDTO();
                        // One DbContext is not thread-safe: run the queries one after another, not with Task.Run.
            stats.TotalStudents = await GetTotalStudentsAsync(collegeId, ayId);
            stats.PassPercentage = await GetPassPercentageAsync(collegeId, ayId);
            stats.TotalExamsConducted = await GetTotalExamsConductedAsync(collegeId, ayId);
            stats.ATKTStudentCount = await GetATKTStudentCountAsync(collegeId, ayId);
            stats.CourseStudentCounts = await GetCourseStudentCountAsync(ayId);
            stats.ExamLifecycle = await GetExamLifecycleAsync(collegeId, ayId);
            return stats;
        }

        public async Task<int> GetTotalStudentsAsync(Guid collegeId, Guid ayId)
        {
            return await _context.StudentEligibilities.Where(se => se.CollegeId == collegeId && se.AYID == ayId && !se.IsDeleted).Select(se => se.StdMstId).Distinct().CountAsync();
        }

        //public async Task<decimal> GetPassPercentageAsync(Guid collegeId, Guid ayId)
        //{
        //    var query = from mm in _context.MarksMasters
        //                join se in _context.StudentEligibilities
        //                    on new { mm.StdMstId, mm.AcademicYearAYID }
        //                    equals new { se.StdMstId, AcademicYearAYID = se.AYID }
        //                where !se.IsDeleted
        //                      && !mm.IsDeleted
        //                      && se.CollegeId == collegeId
        //                      && mm.AcademicYearAYID == ayId
        //                      && mm.OverallRemark != null
        //                select mm;

        //    var totalStudents = await query
        //        .Select(mm => mm.StdMstId)
        //        .Distinct()
        //        .CountAsync();

        //    if (totalStudents == 0) return 0;

        //    var passCount = await query
        //        .Where(mm => mm.OverallRemark.Trim().ToLower() == "pass")
        //        .Select(mm => mm.StdMstId)
        //        .Distinct()
        //        .CountAsync();

        //    return Math.Round((decimal)passCount / totalStudents * 100, 2);
        //}
        public async Task<decimal> GetPassPercentageAsync(Guid collegeId, Guid ayId)
        {
            // Use SQL-like string comparison that EF Core can translate
            var query = from mm in _context.MarksMasters
                        join se in _context.StudentEligibilities
                            on new { mm.StdMstId, mm.AcademicYearAYID }
                            equals new { se.StdMstId, AcademicYearAYID = se.AYID }
                        where !se.IsDeleted
                              && !mm.IsDeleted
                              && se.CollegeId == collegeId
                              && mm.AcademicYearAYID == ayId
                              && mm.OverallRemark != null
                        select mm;

            var totalStudents = await query
                .Select(mm => mm.StdMstId)
                .Distinct()
                .CountAsync();

            if (totalStudents == 0) return 0;

            // Use EF.Functions.Like for string comparison or use SqlMethods
            // Option 1: Use EF.Functions.Like (case insensitive)
            var passCount = await query
                .Where(mm => EF.Functions.Like(mm.OverallRemark, "pass")
                             || EF.Functions.Like(mm.OverallRemark, "Pass"))
                .Select(mm => mm.StdMstId)
                .Distinct()
                .CountAsync();

            return Math.Round((decimal)passCount / totalStudents * 100, 2);
        }

        // FIXED: Renamed from GetTotalExamConductedAsync to GetTotalExamsConductedAsync
        public async Task<int> GetTotalExamsConductedAsync(Guid collegeId, Guid ayId)
        {
            return await _context.Exams.Where(e => e.CollegeId == collegeId && e.AcademicYearAYID == ayId && !e.IsDeleted).Select(e => e.ExamId).Distinct().CountAsync();
        }

        public async Task<int> GetATKTStudentCountAsync(Guid collegeId, Guid ayId)
        {
            return await (from mm in _context.MarksMasters
                          join em in _context.Exams on mm.ExamId equals em.ExamId
                          where !em.IsDeleted && !mm.IsDeleted && em.ExamType.Trim().ToLower() == "a.t.k.t" && mm.AcademicYearAYID == ayId
                      && mm.CollegeId == collegeId && mm.AcademicYearAYID == em.AcademicYearAYID
                          select mm.StdMstId).Distinct().CountAsync();
        }

        // NEW: Get Exam Type Distribution with Appeared/Passed counts


        public async Task<List<ExamTypeDistributionDTO>> GetExamTypeDistributionAsync(
    Guid courseId,
    Guid ayId)
        {
            var query =
                from mm in _context.MarksMasters
                join em in _context.Exams
                    on mm.ExamId equals em.ExamId
                join se in _context.StudentEligibilities
                    on new
                    {
                        mm.StdMstId,
                        AcademicYearAYID = mm.AcademicYearAYID
                    }
                    equals new
                    {
                        se.StdMstId,
                        AcademicYearAYID = se.AYID
                    }
                where !mm.IsDeleted
                      && !em.IsDeleted
                      && !se.IsDeleted
                      && se.CourseId == courseId
                      && mm.AcademicYearAYID == ayId
                      && mm.OverallRemark != null
                group new { mm, em, se }
                by new { mm.SemesterId, em.ExamType }
                into g
                select new ExamTypeDistributionDTO
                {
                    SemesterId = g.Key.SemesterId,
                    ExamType = g.Key.ExamType,

                    Appeared = g
                        .Select(x => x.se.StdMstId)
                        .Distinct()
                        .Count(),

                    Passed = g
                        .Where(x =>
                            x.mm.OverallRemark.Trim().ToLower() == "pass")
                        .Select(x => x.se.StdMstId)
                        .Distinct()
                        .Count()
                };

            var result = await query.ToListAsync();

            return result;
        }


        //public async Task<List<ExamTypeDistributionDTO>> GetExamTypeDistributionAsync(Guid courseId, Guid ayId)
        //{
        //    var result = await (
        //        from mm in _context.MarksMasters
        //        join em in _context.Exams
        //            on mm.ExamId equals em.ExamId
        //        join se in _context.StudentEligibilities
        //            on new { mm.StdMstId, AcademicYearAYID = mm.AcademicYearAYID }
        //            equals new { se.StdMstId, AcademicYearAYID = se.AYID }
        //        where !mm.IsDeleted
        //              && !em.IsDeleted
        //              && !se.IsDeleted
        //              && se.CourseId == courseId
        //              && mm.AcademicYearAYID == ayId
        //              && mm.OverallRemark != null
        //        group new { mm, em, se } by new { mm.SemesterId, em.ExamType } into g
        //        select new ExamTypeDistributionDTO
        //        {
        //            SemesterId = g.Key.SemesterId,
        //            ExamType = g.Key.ExamType,
        //            Appeared = g.Select(x => x.se.StdMstId).Distinct().Count(),
        //            Passed = g
        //                .Where(x => x.mm.OverallRemark.Trim().ToLower() == "pass")
        //                .Select(x => x.se.StdMstId)
        //                .Distinct()
        //                .Count()
        //        }
        //    ).ToListAsync();

        //    return result;
        //}

        public async Task<List<ExamLifecycleDTO>> GetExamLifecycleAsync(
            Guid collegeId,
            Guid ayId)
        {
            // Plain LINQ (not raw SQL) so the global college filter applies: a caller can only ever see the
            // exams of its own college whatever collegeId it sends. DeclareResult is LEFT-joined by exam, so
            // an exam with no row yet (hall tickets not released, nothing declared) still shows its progress.
            var exams = await _context.Exams
                .Where(e => e.AcademicYearAYID == ayId && e.CollegeId == collegeId)
                .Select(e => new { e.ExamId, e.Name, e.CreatedAt })
                .ToListAsync();

            var students = await _context.MarksMasters
                .Where(m => m.AcademicYearAYID == ayId && m.ExamId != null)
                .Select(m => new
                {
                    ExamId = m.ExamId!.Value,
                    m.StdMstId,
                    HasSeat = m.SeatNo != null && m.SeatNo.Trim() != "",
                    HasMarks = m.StudentMarks!.Any(sm => sm.Marks != null)
                })
                .ToListAsync();

            var declares = await _context.DeclareResults
                .Where(d => d.AcademicYear == ayId)
                .Select(d => new { d.ExamId, d.ReleaseHallTicket, d.IsDeclare, d.GazetteGnrt })
                .ToListAsync();

            var studentsByExam = students.ToLookup(s => s.ExamId);
            var declaresByExam = declares.ToLookup(d => d.ExamId);

            return exams
                .Where(e => studentsByExam[e.ExamId].Any())
                .OrderBy(e => e.CreatedAt)
                .Select(e =>
                {
                    var mine = studentsByExam[e.ExamId].ToList();
                    var drs = declaresByExam[e.ExamId].ToList();
                    return new ExamLifecycleDTO
                    {
                        ExamName = e.Name,
                        AssignedStudent = mine.Select(x => x.StdMstId).Distinct().Count(),
                        SeatNo = mine.Where(x => x.HasSeat).Select(x => x.StdMstId).Distinct().Count(),
                        MarksEntered = mine.Where(x => x.HasMarks).Select(x => x.StdMstId).Distinct().Count(),
                        // One exam can have a DeclareResult row per semester: released/declared means any of them.
                        ReleaseHallTicket = drs.Any(d => d.ReleaseHallTicket),
                        IsDeclare = drs.Any(d => d.IsDeclare),
                        GazetteGnrt = drs.Count == 0 ? 0 : drs.Max(d => d.GazetteGnrt)
                    };
                })
                .ToList();
        }

    }
}
//using DocumentFormat.OpenXml.InkML;
//using ExamAPI.Data;
//using ExamAPI.DTOs;
//using ExamAPI.Services.Dashboard;
//using Microsoft.EntityFrameworkCore;

//namespace ExamAPI.Services.Dashboard
//{
//    public class DashboardService : IDashboardService
//    {
//        private readonly ApplicationDbContext _context;

//        public DashboardService(ApplicationDbContext context)
//        {
//            _context = context;
//        }

//        public async Task<List<CourseStudentCountDTO>> GetCourseStudentCountAsync(Guid ayId)
//        {
//            var result = await (
//                from se in _context.StudentEligibilities
//                join cm in _context.CourseMasters
//                    on se.CourseId equals cm.CourseId
//                where !se.IsDeleted
//                      && !cm.IsDeleted
//                      && se.AYID == ayId
//                group se by new { cm.CourseId, cm.Name } into g
//                select new CourseStudentCountDTO
//                {
//                    CourseId = g.Key.CourseId,
//                    CourseName = g.Key.Name,
//                    StudentCount = g.Count()
//                }
//            ).ToListAsync();

//            return result;
//        }


//        public async Task<List<SemesterStudentCountDTO>> GetSemesterWiseStudentCountAsync(Guid courseId, Guid ayId)
//        {
//            var eligibleStudentIds = _context.StudentEligibilities.Where(se => se.CourseId == courseId && !se.IsDeleted).Select(se => se.StdMstId);

//            return await _context.MarksMasters.Where(mm => eligibleStudentIds.Contains(mm.StdMstId) && mm.AcademicYearAYID == ayId && !mm.IsDeleted).GroupBy(mm => mm.SemesterId).Select(g => new SemesterStudentCountDTO
//            {
//                SemesterId = g.Key,
//                StudentCount = g.Select(x => x.StdMstId).Distinct().Count(),
//            }).ToListAsync();
//        }

//        public async Task<List<PassFailChartDTO>> GetPassFailChartAsync(Guid courseId, Guid ayId)
//        {

//            var debugData = await (
//    from mm in _context.MarksMasters
//    join se in _context.StudentEligibilities
//        on mm.StdMstId equals se.StdMstId
//    where !se.IsDeleted
//          && !mm.IsDeleted
//          && mm.AcademicYearAYID == se.AYID
//          && se.CourseId == courseId
//          && mm.AcademicYearAYID == ayId
//    select new
//    {
//        mm.SemesterId,
//        mm.OverallRemark
//    }
//).ToListAsync();

//            var result = await (
//                from mm in _context.MarksMasters
//                join se in _context.StudentEligibilities
//                    on mm.StdMstId equals se.StdMstId
//                where !se.IsDeleted
//                      && !mm.IsDeleted
//                      && mm.AcademicYearAYID == se.AYID
//                      && se.CourseId == courseId
//                      && mm.AcademicYearAYID == ayId
//                group mm by mm.SemesterId into g
//                select new PassFailChartDTO
//                {
//                    SemesterId = g.Key,
//                    PassCount = g.Count(x =>
//                        x.OverallRemark != null &&
//                        x.OverallRemark.Trim().ToLower() == "pass"),

//                    FailCount = g.Count(x =>
//                        x.OverallRemark != null &&
//                        x.OverallRemark.Trim().ToLower() == "fail")
//                }
//            ).ToListAsync();

//            return result;
//        }

//        public async Task<List<SemesterExamTypeCountDTO>> GetSemesterWiseExamTypeCountAsync(Guid courseId, Guid ayId)
//        {
//            var result = await (
//                from mm in _context.MarksMasters
//                join em in _context.Exams
//                    on mm.ExamId equals em.ExamId
//                join se in _context.StudentEligibilities
//                    on new { em.Semester, em.AcademicYearAYID }
//                    equals new { Semester = se.SemesterId, AcademicYearAYID = se.AYID }
//                where !mm.IsDeleted
//                      && !em.IsDeleted
//                      && !se.IsDeleted
//                      && mm.AcademicYearAYID == em.AcademicYearAYID
//                      && se.CourseId == courseId
//                      && mm.AcademicYearAYID == ayId
//                group se by new { mm.SemesterId, em.ExamType } into g
//                select new SemesterExamTypeCountDTO
//                {
//                    SemesterId = g.Key.SemesterId,
//                    ExamType = g.Key.ExamType,
//                    StudentCount = g.Select(x => x.StdMstId).Distinct().Count()
//                }
//            ).ToListAsync();

//            return result;
//        }


//        public async Task<DashboardStatsDTO> GetDashboardStatsAsync(Guid collegeId, Guid ayId)
//        {
//            var stats = new DashboardStatsDTO();
//            var tasks = new List<Task>();

//            tasks.Add(Task.Run(async () =>
//            stats.TotalStudents = await GetTotalStudentsAsync(collegeId, ayId)));

//            tasks.Add(Task.Run(async () =>
//            stats.PassPercentage = await GetPassPercentageAsync(collegeId, ayId)));

//            tasks.Add(Task.Run(async () =>
//            stats.TotalExamsConducted = await GetTotalExamsConductedAsync(collegeId, ayId)));

//            tasks.Add(Task.Run(async () =>
//            stats.ATKTStudentCount = await GetATKTStudentCountAsync(collegeId, ayId)));

//            tasks.Add(Task.Run(async () =>
//            stats.CourseStudentCounts = await GetCourseStudentCountAsync(ayId)));

//            tasks.Add(Task.Run(async () =>
//            stats.ExamLifecycle = await GetExamLifecycleAsync(collegeId, ayId)));

//            await Task.WhenAll(tasks);
//            return stats;
//        }

//        public async Task<int> GetTotalStudentsAsync(Guid collegeId, Guid ayId)
//        {
//            return await _context.StudentEligibilities.Where(se => se.CollegeId == collegeId && se.AYID == ayId && !se.IsDeleted).Select(se => se.StdMstId).Distinct().CountAsync();
//        }

//        public async Task<decimal> GetPassPercentageAsync(Guid collegeId, Guid ayId)
//        {
//            //var query= from mm in _context.MarksMasters join se in _context.StudentEligibilities on mm.StdMstId equals se.StdMstId where !mm.IsDeleted && !se.IsDeleted && mm.AcademicYearAYID==se.AYID && se.CollegeId==collegeId && se.AYID==ayId select 

//            var query= from mm in _context.MarksMasters
//                       join se in _context.StudentEligibilities
//                           on new { mm.StdMstId, mm.AcademicYearAYID }
//                           equals new { se.StdMstId, AcademicYearAYID = se.AYID }
//                       where !se.IsDeleted
//                             && !mm.IsDeleted
//                             && se.CollegeId == collegeId
//                             && mm.AcademicYearAYID == ayId
//                             && mm.OverallRemark != null
//                       select mm;
//            var totalStudents = await query
//                .Select(mm => mm.StdMstId)
//                .Distinct()
//                .CountAsync();

//            if (totalStudents == 0) return 0;

//            var passCount = await query
//                .Where(mm => mm.OverallRemark.Trim().ToLower() == "pass")
//                .Select(mm => mm.StdMstId)
//                .Distinct()
//                .CountAsync();

//            return Math.Round((decimal)passCount / totalStudents * 100, 2);
//        }

//        public async Task<int> GetTotalExamsConductedAsync(Guid collegeId, Guid ayId)
//        {
//            return await _context.Exams.Where(e => e.CollegeId == collegeId && e.AcademicYearAYID == ayId && !e.IsDeleted).Select(e => e.ExamType).Distinct().CountAsync();
//        }

//        public async Task<int> GetATKTStudentCountAsync(Guid collegeId, Guid ayId)
//        {
//            return await (from mm in _context.MarksMasters
//                          join em in _context.Exams on mm.ExamId equals em.ExamId
//                          where !em.IsDeleted && !mm.IsDeleted && em.ExamType.Trim().ToLower() == "a.t.k.t" && mm.AcademicYearAYID == ayId
//                      && mm.CollegeId == collegeId && mm.AcademicYearAYID == em.AcademicYearAYID select mm.StdMstId).Distinct().CountAsync();

//        }

//        public async Task<ExamLifecycleDTO> GetExamLifecycleAsync(Guid collegeId, Guid ayId)
//        {
//            // Get active exam
//            var sql = @"
//        SELECT 
//            em.Name AS ExamName,
//            COUNT(DISTINCT mm.StdMstId) AS AssignedStudent,
//            COUNT(DISTINCT CASE WHEN mm.SeatNo IS NOT NULL AND LTRIM(RTRIM(mm.SeatNo)) <> '' THEN mm.StdMstId END) AS SeatNo,
//            dr.ReleaseHallTicket AS ReleaseHallTicket,
//            COUNT(DISTINCT CASE WHEN sm.Marks IS NOT NULL AND LTRIM(RTRIM(sm.Marks)) <> '' THEN sm.MarksId END) AS MarksEntered,
//            dr.GazetteGnrt AS GazetteGnrt,
//            dr.IsDeclare AS IsDeclare
//        FROM MarksMaster mm
//        INNER JOIN ExamMaster em ON mm.ExamId = em.ExamId AND em.AcademicYearAYID = mm.AcademicYearAYID
//        INNER JOIN DeclareResult dr ON em.CourseId = dr.CourseId AND em.AcademicYearAYID = dr.AcademicYear
//        INNER JOIN StudentMarks sm ON sm.MarksId = mm.MarksId
//        WHERE em.CollegeId = {0}
//            AND em.AcademicYearAYID = {1}
//            AND em.IsDeleted = 0
//            AND mm.IsDeleted = 0
//            AND sm.IsDeleted = 0
//            AND em.ResultDeclared = 0
//        GROUP BY em.Name, dr.ReleaseHallTicket, dr.GazetteGnrt, dr.IsDeclare";

//            var result = await _context.Database
//                .SqlQueryRaw<ExamLifecycleDTO>(sql, collegeId, ayId)
//                .FirstOrDefaultAsync();

//            if (result == null)
//            {
//                // Get completed exam if no active exam
//                var completedSql = @"
//            SELECT TOP 1
//                em.Name AS ExamName,
//                COUNT(DISTINCT mm.StdMstId) AS AssignedStudent,
//                COUNT(DISTINCT CASE WHEN mm.SeatNo IS NOT NULL AND LTRIM(RTRIM(mm.SeatNo)) <> '' THEN mm.StdMstId END) AS SeatNo,
//                dr.ReleaseHallTicket AS ReleaseHallTicket,
//                COUNT(DISTINCT CASE WHEN sm.Marks IS NOT NULL AND LTRIM(RTRIM(sm.Marks)) <> '' THEN sm.MarksId END) AS MarksEntered,
//                dr.GazetteGnrt AS GazetteGnrt,
//                dr.IsDeclare AS IsDeclare
//            FROM MarksMaster mm
//            INNER JOIN ExamMaster em ON mm.ExamId = em.ExamId AND em.AcademicYearAYID = mm.AcademicYearAYID
//            INNER JOIN DeclareResult dr ON em.CourseId = dr.CourseId AND em.AcademicYearAYID = dr.AcademicYear
//            INNER JOIN StudentMarks sm ON sm.MarksId = mm.MarksId
//            WHERE em.CollegeId = {0}
//                AND em.AcademicYearAYID = {1}
//                AND em.IsDeleted = 0
//                AND mm.IsDeleted = 0
//                AND sm.IsDeleted = 0
//                AND em.ResultDeclared = 1
//            GROUP BY em.Name, dr.ReleaseHallTicket, dr.GazetteGnrt, dr.IsDeclare
//            ORDER BY em.CreatedDate DESC";

//                var completedResult = await _context.Database
//                    .SqlQueryRaw<ExamLifecycleDTO>(completedSql, collegeId, ayId)
//                    .FirstOrDefaultAsync();

//                if (completedResult == null)
//                {
//                    return new ExamLifecycleDTO
//                    {
//                        ExamName = "No Exam Found",
//                        AssignedStudent = 0,
//                        SeatNo = 0,
//                        ReleaseHallTicket = 0,
//                        MarksEntered = 0,
//                        GazetteGnrt = 0,
//                        IsDeclare = 0
//                    };
//                }

//                return completedResult;
//            }

//            return result;
//        }
//    }
//}
