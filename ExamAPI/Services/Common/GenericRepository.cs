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
            // Look the row up through the query (FirstOrDefaultAsync), not FindAsync: FindAsync goes by key and
            // bypasses the global query filters, so an id from another college would soft-delete that college's row.
            var keyName = _context.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties[0].Name;
            var entity = await _context.Set<T>().FirstOrDefaultAsync(e => EF.Property<Guid>(e, keyName) == id);

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