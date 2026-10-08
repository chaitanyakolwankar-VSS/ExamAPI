using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Wordprocessing;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileSystemGlobbing.Internal;
using Parlot.Fluent;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ExamAPI.Services.StudentAssignRpt
{
    public class StudentAssignRptService : IStudentAssignRptService
    {
        private readonly ApplicationDbContext _context;

        public StudentAssignRptService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<StudentAssignRptResDto>> GetReportAsync(StudentAssignRptDto request)
        {
            //select distinct subm.SubjectCode, subm.Name,mm.StudentID,mm.SeatNo,sm.FirstName + ' ' + sm.LastName[Student Name] from MarksMaster mm,StudentMaster sm, SubjectMaster subm where mm.StdMstId = sm.StdMstId and subm.CourseId = '89e43b6c-a4ca-40bb-b93e-4bf47844c80e' and subm.Pattern = 'NEP' and subm.SemId = 'Sem-6' and mm.ExamId = '872f34f3-2ceb-455b-9c07-52e019ec6a57' group by subm.SubjectCode, subm.Name,mm.StudentID,mm.SeatNo,sm.FirstName + ' ' + sm.LastName order by StudentID desc

            //Making a single line code
            //SELECT mm.StudentID,mm.SeatNo,sm.FirstName + ' ' + sm.LastName AS[Student Name],STRING_AGG(subm.SubjectCode + ' - ' + subm.Name, ', ') AS Subjects FROM MarksMaster mm INNER JOIN StudentMaster sm ON mm.StdMstId = sm.StdMstId INNER JOIN SubjectMaster subm ON subm.CourseId = '89e43b6c-a4ca-40bb-b93e-4bf47844c80e' AND subm.Pattern = 'NEP' AND subm.SemId = 'Sem-6' WHERE mm.ExamId = '872f34f3-2ceb-455b-9c07-52e019ec6a57' GROUP BY mm.StudentID, mm.SeatNo, sm.FirstName, sm.LastName ORDER BY mm.StudentID DESC;

            var query = from mm in _context.MarksMasters
                        join sm in _context.StudentMasters on mm.StdMstId equals sm.StdMstId
                        from subm in _context.SubjectMasters
                        where subm.CourseId == request.CourseId && subm.Pattern == request.Pattern && subm.SemId == request.Semester && mm.ExamId == request.ExamId
                        select new StudentAssignRptResDto
                        {
                            SubjectCode = subm.SubjectCode,
                            SubjectName = subm.Name,
                            StudentID = mm.StudentID,
                            SeatNo = mm.SeatNo,
                            Name = sm.FirstName + " " + sm.LastName
                        };
            return await query
        .Distinct()
        .OrderByDescending(x => x.StudentID)
        .AsNoTracking()
        .ToListAsync();
        }


        public async Task<List<StudentCreditRptResponseDto>> GetCreditReportAsync(StudentAssignRptDto request)
        {

            //select distinct subm.SubjectCode, subm.Name,subcm.TotalCredits,sc.Head,sc.HeadType,sc.HeadOutOf,mm.StudentID,mm.SeatNo,sm.FirstName + ' ' + sm.LastName[Student Name],case when subcm.PassingStrategy = 'Combined' then sc.HeadFormula when subcm.PassingStrategy = 'HeadWise' then sc.HeadPass end as [Pass Value] from MarksMaster mm,StudentMaster sm, SubjectMaster subm,SubjectCreditMaster subcm, SubjectCredits sc where sc.CreditsId = subcm.CreditsId and subcm.SubjectId = subm.SubjectId and mm.StdMstId = sm.StdMstId and subm.CourseId = '89e43b6c-a4ca-40bb-b93e-4bf47844c80e' and subm.Pattern = 'NEP' and subm.SemId = 'Sem-6' and mm.ExamId = '872f34f3-2ceb-455b-9c07-52e019ec6a57' group by subm.SubjectCode, subm.Name,mm.StudentID,mm.SeatNo,sm.FirstName + ' ' + sm.LastName,subcm.TotalCredits,sc.Head,sc.HeadFormula,sc.HeadType,sc.HeadOutOf,sc.HeadPass,subcm.PassingStrategy order by StudentID desc


            var query =
                from mm in _context.MarksMasters
                join sm in _context.StudentMasters on mm.StdMstId equals sm.StdMstId
                from subm in _context.SubjectMasters
                join subcm in _context.SubjectCreditMasters on subm.SubjectId equals subcm.SubjectId
                join sc in _context.SubjectCredits on subcm.CreditsId equals sc.CreditsId
                where subm.CourseId == request.CourseId
                      && subm.Pattern == request.Pattern
                      && subm.SemId == request.Semester
                      && mm.ExamId == request.ExamId
                select new StudentCreditRptResponseDto
                {
                    SubjectCode = subm.SubjectCode,
                    SubjectName = subm.Name,
                    TotalCredits = subcm.TotalCredits,
                    Head = sc.Head,
                    HeadFormula = sc.HeadFormula,
                    HeadType = sc.HeadType,
                    HeadOutOf = sc.HeadOutOf,
                    HeadPass = sc.HeadPass,
                    StudentID = mm.StudentID,
                    SeatNo = mm.SeatNo,
                    Name = sm.FirstName + " " + sm.LastName
                };

            return await query
                .Distinct()
                .OrderByDescending(x => x.StudentID)
                .AsNoTracking()
                .ToListAsync();
        }


    }
}
