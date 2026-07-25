
﻿using ExamAPI.DTOs;
using Microsoft.AspNetCore.Mvc;


namespace ExamAPI.Services.StudentMasters
{
    public interface IStudentMasterService

    {
         Task<List<StudentMasterDto>> GetDataAsync([FromQuery] Guid ayid);
        //Task<string> SaveStudentAsync(Savedata dto); 
        Task<string> SaveStudentAsync(Savedata dto);
        Task<List<FetchData>> GetbycourseAsync(Guid courseId, Guid ayid);
        Task<List<FetchData>> SearchStudentsAsync(Searchbyname model, Guid ayid);

        Task<Savedata> GetStudentByIdAsync(string studentId, Guid ayid);
        Task<string> UpdateStudentAsync(Savedata dto);
        Task<string> DeleteStudentAsync(string studentId);
        Task<(byte[] FileBytes, string FileName)> GenerateExcelTemplateAsync( StudExcelDto dto);

        Task<object> ImportStudentsAsync(StudentImportDto dto);
        Task<List<ExamDetailsResultDto>> GetExamDetailsAsync(string studentId);
        Task<string> RestoreExamAsync(string studentId, Guid marksId);
        Task<string> DeleteExamAsync(string studentId, Guid marksId);
        
    }
}
