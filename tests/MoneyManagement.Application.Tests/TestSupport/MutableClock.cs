using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.TestSupport;

/// <summary>
/// A deterministic <see cref="IDateTimeProvider"/> whose "now" can be moved
/// forward mid-test. The pools slice needs it because close and pay are
/// deliberately separate steps on DIFFERENT days, and <c>PoolUnitEvent.Settle</c>
/// rejects a future settlement date — so the only way to exercise the two-phase
/// distribution is to actually advance the clock between them.
/// </summary>
internal sealed class MutableClock(DateTime utcNow) : IDateTimeProvider
{
    public DateTime UtcNow { get; private set; } = utcNow;

    public DateOnly Today => DateOnly.FromDateTime(UtcNow);

    public void Advance(int days) => UtcNow = UtcNow.AddDays(days);
}
