using ExamAPI.Models;
using System.Linq.Expressions;

namespace ExamAPI.Services.Common
{
    public interface IGenericRepository
    {
        Task DeleteAsync<T>(Guid id) where T : BaseEntity;
        Task DeleteRangeAsync<T>(
             Expression<Func<T, bool>> predicate
         ) where T : BaseEntity;

    }
}