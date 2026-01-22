using ExamAPI.Data;
using ExamAPI.Models;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;


namespace ExamAPI.Services.Common
{
    public class GenericRepository : IGenericRepository
    {
        private readonly ApplicationDbContext _context;

        public GenericRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task DeleteAsync<T>(Guid id) where T : BaseEntity
        {
            var entity = await _context.Set<T>().FindAsync(id);

            if (entity != null)
            {
                entity.IsDeleted = true;
                entity.DeletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
        public async Task DeleteRangeAsync<T>( Expression<Func<T, bool>> predicate  ) where T : BaseEntity
        {
            var entities = await _context.Set<T>()
                .Where(predicate)
                .ToListAsync();

            foreach (var entity in entities)
            {
                entity.IsDeleted = true;
                entity.DeletedAt = DateTime.UtcNow;
            }
        }

    }
}