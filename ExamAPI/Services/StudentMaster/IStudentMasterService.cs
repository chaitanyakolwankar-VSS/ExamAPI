using ExamAPI.DTOs;

namespace ExamAPI.Services.StudentMaster
{
    public interface IStudentMasterService

    {
         Task<List<StudentMasterDto>> GetDataAsync();
        //Task<string> SaveStudentAsync(Savedata dto); 
        Task<string> SaveStudentAsync(Savedata dto);
        Task<List<FetchData>> GetbycourseAsync(Guid courseId);
        Task<List<FetchData>> SearchStudentsAsync(Searchbyname model);
    }
}
