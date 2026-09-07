using FluentAssertions;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.Pools;

public class PoolParticipantTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedNow);
    private static readonly DateOnly Inception = new(2026, 4, 1);
    private static readonly Guid PoolId = Guid.CreateVersion7();

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private static IDateTimeProvider Clock() => new FixedClock(FixedNow);

    private static Result<PoolParticipant> Create(
        Guid? poolId = null,
        string name = "Ion",
        bool isOwner = false,
        DateOnly? joinedOn = null,
        DateOnly? poolInceptionDate = null) =>
        PoolParticipant.Create(
            poolId ?? PoolId,
            name,
            isOwner,
            joinedOn ?? Inception,
            poolInceptionDate ?? Inception,
            Clock());

    [Fact]
    public void Create_Friend_Succeeds()
    {
        Result<PoolParticipant> result = Create(name: "Ion", joinedOn: new DateOnly(2026, 4, 15));

        result.IsSuccess.Should().BeTrue();
        PoolParticipant participant = result.Value;
        participant.Id.Should().NotBe(Guid.Empty);
        participant.PoolId.Should().Be(PoolId);
        participant.Name.Should().Be("Ion");
        participant.IsOwner.Should().BeFalse();
        participant.JoinedOn.Should().Be(new DateOnly(2026, 4, 15));
        participant.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Create_Owner_Succeeds()
    {
        // The owner holds REAL units, not a residual - which is what makes
        // sum(participantUnits) == totalUnits an assertable identity.
        Result<PoolParticipant> result = Create(name: "Me", isOwner: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsOwner.Should().BeTrue();
    }

    [Fact]
    public void Create_EmptyPoolId_ReturnsPoolNotFound()
    {
        Result<PoolParticipant> result = Create(poolId: Guid.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.NotFound(Guid.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_ReturnsParticipantNameRequired(string name)
    {
        Result<PoolParticipant> result = Create(name: name);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.ParticipantNameRequired);
    }

    [Fact]
    public void Create_NameOver100Chars_ReturnsParticipantNameTooLong()
    {
        Result<PoolParticipant> result = Create(name: new string('I', 101));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.ParticipantNameTooLong);
    }

    [Fact]
    public void Create_TrimsName()
    {
        Result<PoolParticipant> result = Create(name: "  Ion  ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Ion");
    }

    [Fact]
    public void Create_JoinedOnInFuture_ReturnsParticipantJoinedOnInFuture()
    {
        Result<PoolParticipant> result = Create(joinedOn: Today.AddDays(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.ParticipantJoinedOnInFuture);
    }

    [Fact]
    public void Create_JoinedOnBeforeInception_ReturnsParticipantJoinedBeforeInception()
    {
        Result<PoolParticipant> result = Create(joinedOn: Inception.AddDays(-1));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.ParticipantJoinedBeforeInception);
    }

    [Fact]
    public void Rename_UpdatesTrimmedName()
    {
        PoolParticipant participant = Create().Value;

        Result result = participant.Rename("  Ion P.  ");

        result.IsSuccess.Should().BeTrue();
        participant.Name.Should().Be("Ion P.");
    }

    [Fact]
    public void Rename_BlankName_FailsAndLeavesNameUnchanged()
    {
        PoolParticipant participant = Create(name: "Ion").Value;

        Result result = participant.Rename("  ");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.ParticipantNameRequired);
        participant.Name.Should().Be("Ion");
    }

    [Fact]
    public void Archive_WithZeroUnits_Succeeds()
    {
        PoolParticipant participant = Create().Value;

        Result result = participant.Archive(unitsHeld: 0m);

        result.IsSuccess.Should().BeTrue();
        participant.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Archive_WhileHoldingUnits_ReturnsParticipantHoldsUnits()
    {
        // Deliberately NOT the unconditional Loan.Archive() convention: a hidden
        // participant drops out of sum(participantUnits) and their stake is
        // silently absorbed by the owner.
        PoolParticipant participant = Create().Value;

        Result result = participant.Archive(unitsHeld: 1_000m);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.ParticipantHoldsUnits);
        participant.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Archive_WithSubQuantumUnitDust_Succeeds()
    {
        PoolParticipant participant = Create().Value;

        Result result = participant.Archive(unitsHeld: 0.0000000000001m);

        result.IsSuccess.Should().BeTrue();
        participant.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Archive_Owner_ReturnsOwnerCannotBeArchived()
    {
        // The filtered unique index allows exactly one owner row per pool, so
        // archiving it would orphan the pool with no way to appoint a successor.
        PoolParticipant owner = Create(name: "Me", isOwner: true).Value;

        Result result = owner.Archive(unitsHeld: 0m);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.OwnerCannotBeArchived);
        owner.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Archive_IsIdempotent()
    {
        PoolParticipant participant = Create().Value;
        participant.Archive(0m);

        Result result = participant.Archive(0m);

        result.IsSuccess.Should().BeTrue();
        participant.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Unarchive_IsIdempotent()
    {
        PoolParticipant participant = Create().Value;
        participant.Archive(0m);

        participant.Unarchive().IsSuccess.Should().BeTrue();
        participant.IsArchived.Should().BeFalse();

        participant.Unarchive().IsSuccess.Should().BeTrue();
        participant.IsArchived.Should().BeFalse();
    }
}
