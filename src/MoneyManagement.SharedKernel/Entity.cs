namespace MoneyManagement.SharedKernel;

/// <summary>
/// Base class for aggregate roots. Tracks domain events so the
/// infrastructure layer can dispatch them after a successful save.
/// </summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity(Guid id) => Id = id;

    // Required by EF Core materialization.
    protected Entity() { }

    public Guid Id { get; protected set; }

    // Audit fields - assigned by AuditableEntitySaveChangesInterceptor (UTC).
    public DateTime CreatedAt { get; internal set; }
    public DateTime UpdatedAt { get; internal set; }

    public IReadOnlyList<IDomainEvent> GetDomainEvents() => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
