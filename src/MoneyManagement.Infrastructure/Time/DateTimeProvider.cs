using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure.Time;

internal sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
