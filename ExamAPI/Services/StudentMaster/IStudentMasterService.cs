<<<<<<< HEAD
﻿using ClosedXML.Excel;
using ExamAPI.DTOs;
using static ExamAPI.DTOs.StudentExcelDto;
=======
﻿using ExamAPI.DTOs;
>>>>>>> 49b1e581466adc2308420e40d667e717b5a343fa

namespace ExamAPI.Services.StudentMaster
{
    public interface IStudentMasterService

    {
         Task<List<StudentMasterDto>> GetDataAsync();
        //Task<string> SaveStudentAsync(Savedata dto); 
        Task<string> SaveStudentAsync(Savedata dto);
        Task<List<FetchData>> GetbycourseAsync(Guid courseId);
        Task<List<FetchData>> SearchStudentsAsync(Searchbyname model);
<<<<<<< HEAD
        Task<Savedata> GetStudentByIdAsync(string studentId);
        Task<string> UpdateStudentAsync(Savedata dto);
        Task<string> DeleteStudentAsync(string studentId);
        Task<(byte[] FileBytes, string FileName)> GenerateExcelTemplateAsync(Guid courseId, int semesterId);

        Task<object> ImportStudentsAsync(StudentImportDto dto);
      
=======
>>>>>>> 49b1e581466adc2308420e40d667e717b5a343fa
    }
}
