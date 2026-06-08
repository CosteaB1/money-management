using System.Collections;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MoneyManagement.Application.Tests.TestSupport;

/// <summary>
/// Concrete <see cref="DbSet{TEntity}"/> backed by an in-memory list, with an
/// <see cref="IAsyncQueryProvider"/> so EF Core extension methods
/// (<c>ToListAsync</c>, <c>FirstOrDefaultAsync</c>, etc.) work in unit tests.
/// </summary>
internal sealed class FakeDbSet<T> : DbSet<T>, IQueryable<T>, IAsyncEnumerable<T>
    where T : class
{
    private readonly List<T> _items;
    private readonly TestAsyncEnumerable<T> _queryable;

    public FakeDbSet(IEnumerable<T> items)
    {
        _items = [.. items];
        _queryable = new TestAsyncEnumerable<T>(_items);
    }

    IQueryProvider IQueryable.Provider => ((IQueryable<T>)_queryable).Provider;
    Expression IQueryable.Expression => ((IQueryable<T>)_queryable).Expression;
    Type IQueryable.ElementType => ((IQueryable<T>)_queryable).ElementType;
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Add(T entity)
    {
        _items.Add(entity);
        return null!;
    }

    public override void AddRange(IEnumerable<T> entities) => _items.AddRange(entities);

    public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Remove(T entity)
    {
        _items.Remove(entity);
        return null!;
    }

    public override void RemoveRange(IEnumerable<T> entities)
    {
        foreach (T item in entities.ToList())
        {
            _items.Remove(item);
        }
    }

    public override IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        ((IAsyncEnumerable<T>)_queryable).GetAsyncEnumerator(cancellationToken);

    // Required by DbSet<T>; never inspected by the queries we test.
    public override IEntityType EntityType =>
        throw new NotSupportedException("FakeDbSet does not expose an EF Core IEntityType.");
}
