namespace MoneyManagement.Domain.Categories;

/// <summary>
/// Where a <see cref="CategoryPattern"/> originated. <c>Seeded</c> rows come
/// from the built-in keyword rules backfilled at startup; <c>Learned</c> rows
/// are added from observed categorizations.
/// </summary>
public enum CategoryPatternSource
{
    Seeded = 0,
    Learned = 1,
}
