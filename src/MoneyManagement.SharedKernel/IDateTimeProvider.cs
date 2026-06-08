namespace MoneyManagement.SharedKernel;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
