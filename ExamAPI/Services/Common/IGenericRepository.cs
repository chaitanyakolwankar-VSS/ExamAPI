using ExamAPI.Models;

namespace ExamAPI.Services.Common
{
    public interface IGenericRepository
    {
        Task DeleteAsync<T>(Guid id) where T : BaseEntity;
    }
}