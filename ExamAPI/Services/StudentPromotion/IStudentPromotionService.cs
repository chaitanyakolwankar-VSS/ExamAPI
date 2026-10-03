using Azure;
using ExamAPI.DTOs;

namespace ExamAPI.Services.StudentPromotion
{
    public interface IStudentPromotionService
    {
         Task<List<SingleStudentData>> GetStudentData(SingleStudentDataRequest dto);
        Task<EligibilityStudentResponse> GetAssignedStudent(EligibilityStudentsAssign dto);
        Task<ApiResponseDto<object>> SaveEligibility(SaveEligibility dto);

        Task<ApiResponseDto<object>> UpdateEligibility(UpdateEligibility dto);

    }
}
