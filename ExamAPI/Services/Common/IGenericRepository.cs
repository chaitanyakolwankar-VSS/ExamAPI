using ExamAPI.Models;
using System.Linq.Expressions;

namespace ExamAPI.Services.Common
{
    public interface IGenericRepository
    {
        /// <summary>Soft-deletes one row and saves.</summary>
        Task DeleteAsync<T>(Guid id) where T : BaseEntity;

        /// <summary>
        /// Marks the matching rows deleted WITHOUT saving: callers use it inside their own transaction and
        /// save together with the rest of their change (SaveChangesAsync, then commit).
        /// </summary>
        Task DeleteRangeAsync<T>(
             Expression<Func<T, bool>> predicate
         ) where T : BaseEntity;

    }
}