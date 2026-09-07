using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Pools.UnarchivePool;

/// <summary>
/// Puts an archived pool back in service. The counterpart loans and accounts
/// have both had from the start, and which pools were missing.
/// <para>
/// <b>Why pools need it too.</b> Archiving is not a soft "hide" here: one pool
/// per account is enforced forever by <c>ix_pools_account_id</c>, which is
/// deliberately NOT filtered on <c>is_archived</c>. So an accidental archive
/// used to be terminal in both directions — the pool could not come back, and no
/// replacement pool could be created on that account either. The account was
/// left able to hold a pool in the schema and unable to have one in practice.
/// </para>
/// <para>
/// <b>Money-safe by construction.</b> It flips one boolean and touches nothing
/// else. Net worth does not move: <c>PoolAccountOwnershipSource</c> already
/// reads archived pools with <c>IgnoreQueryFilters()</c>, precisely so past
/// dates keep the fraction they really had. And the direction of travel is
/// towards MORE restriction, not less — an active pool re-arms every row of the
/// guard table on its account. That is the mirror image of
/// <c>Pool.Archive</c>, which has to check for outside units because it
/// RELEASES the account.
/// </para>
/// <para>
/// Idempotent: unarchiving an already-active pool is a no-op success, and the
/// lookup uses <c>IgnoreQueryFilters</c> so the archived rows this exists for
/// are actually findable — the <c>UnarchiveLoan</c> contract exactly.
/// </para>
/// </summary>
public sealed record UnarchivePoolCommand(Guid Id) : ICommand;
