using ExamAPI.Models;
using System.Linq.Expressions;

namespace ExamAPI.Services.Common
{
    /// <summary>
    /// The one ordering rule for subjects, used everywhere subjects are listed (drop-downs, marks entry,
    /// hall tickets, marksheets, gazette, results grid/Excel/PDF, reports):
    /// <c>SortOrder</c> ascending with NULLs last, then <c>SubjectCode</c> (ordinal, ignoring case), then Name.
    /// Staff set <see cref="SubjectMaster.SortOrder"/> on Subject Master; duplicates are allowed and
    /// simply fall back to the code.
    /// </summary>
    public static class SubjectOrder
    {
        /// <summary>
        /// Database-side ordering (translates to SQL). Prefer this when the query still selects
        /// <see cref="SubjectMaster"/> rows; otherwise project <c>SortOrder</c> and use the in-memory overload.
        /// </summary>
        public static IOrderedQueryable<SubjectMaster> InSubjectOrder(this IQueryable<SubjectMaster> subjects)
            => subjects
                .OrderBy(s => s.SortOrder == null)
                .ThenBy(s => s.SortOrder)
                .ThenBy(s => s.SubjectCode)
                .ThenBy(s => s.Name);

        /// <summary>Like <see cref="ThenInSubjectOrder{T}"/> but as the primary ordering of a projected query.</summary>
        public static IOrderedQueryable<T> OrderBySubject<T>(
            this IQueryable<T> rows,
            Expression<Func<T, int?>> sortOrder,
            Expression<Func<T, string>> code,
            Expression<Func<T, string>> name)
        {
            var nullsLast = Expression.Lambda<Func<T, bool>>(
                Expression.Equal(sortOrder.Body, Expression.Constant(null, typeof(int?))), sortOrder.Parameters);
            return rows.OrderBy(nullsLast).ThenBy(sortOrder).ThenBy(code).ThenBy(name);
        }

        /// <summary>
        /// Appends the subject-order tiebreakers to an existing ordering (e.g. "by student, then subject order").
        /// Translates to SQL, so use it on queries that project <c>SortOrder</c>, code and name.
        /// </summary>
        public static IOrderedQueryable<T> ThenInSubjectOrder<T>(
            this IOrderedQueryable<T> rows,
            Expression<Func<T, int?>> sortOrder,
            Expression<Func<T, string>> code,
            Expression<Func<T, string>> name)
        {
            var nullsLast = Expression.Lambda<Func<T, bool>>(
                Expression.Equal(sortOrder.Body, Expression.Constant(null, typeof(int?))), sortOrder.Parameters);
            return rows.ThenBy(nullsLast).ThenBy(sortOrder).ThenBy(code).ThenBy(name);
        }

        /// <summary>
        /// In-memory ordering for projected rows that carry the subject's SortOrder and code (and optionally
        /// name). Call after materialising (ToList) or on already-loaded data.
        /// </summary>
        public static IOrderedEnumerable<T> InSubjectOrder<T>(
            this IEnumerable<T> rows,
            Func<T, int?> sortOrder,
            Func<T, string?> code,
            Func<T, string?>? name = null)
        {
            var ordered = rows
                .OrderBy(r => sortOrder(r).HasValue ? 0 : 1)
                .ThenBy(r => sortOrder(r) ?? int.MaxValue)
                .ThenBy(r => code(r) ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            return name == null
                ? ordered
                : ordered.ThenBy(r => name(r) ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Comparer form of the same rule, for List.Sort / SortedSet and friends.</summary>
        public static int Compare(int? orderA, string? codeA, string? nameA, int? orderB, string? codeB, string? nameB)
        {
            if (orderA.HasValue != orderB.HasValue)
                return orderA.HasValue ? -1 : 1;                       // NULLs last
            if (orderA.HasValue)
            {
                var byOrder = orderA.Value.CompareTo(orderB!.Value);
                if (byOrder != 0) return byOrder;
            }
            var byCode = StringComparer.OrdinalIgnoreCase.Compare(codeA ?? string.Empty, codeB ?? string.Empty);
            if (byCode != 0) return byCode;
            return StringComparer.OrdinalIgnoreCase.Compare(nameA ?? string.Empty, nameB ?? string.Empty);
        }

        /// <summary>Largest valid order number accepted by Subject Master.</summary>
        public const int MaxOrder = 999;
    }
}
