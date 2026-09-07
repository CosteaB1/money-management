using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Pools.DeletePool;

/// <summary>
/// HARD-deletes a pool created by mistake, together with its participants and
/// its unit events. The escape hatch archiving is not: archiving hides a pool
/// forever, and the hidden row keeps holding its account through
/// <c>pools.account_id</c> (<c>ON DELETE RESTRICT</c>), so a pool typed in by
/// accident used to make its account permanently undeletable with no way back.
/// <para>
/// <b>Allowed only when the pool provably cannot hold anyone else's money and
/// provably never moved any:</b>
/// <list type="bullet">
/// <item>no NON-OWNER participant holds units — the same count
/// <c>Pool.Archive</c> is judged on, so there is no third-party stake to
/// destroy; and</item>
/// <item>no unit event carries <c>Cash</c> or a <c>MovementTransactionId</c> —
/// so no event of this pool is the bookkeeping half of a real money row.</item>
/// </list>
/// In practice that is exactly "a seed and nothing else": the created-by-mistake
/// shape. Anything past it is wound down by redeeming and then archiving, and is
/// refused with <see cref="Domain.Pools.PoolErrors.DeleteHasMovements"/> (409).
/// </para>
/// <para>
/// <b>Works on archived pools too</b> (<c>IgnoreQueryFilters</c>) — the stuck
/// ones are precisely the archived ones, since archiving is the only exit the
/// slice offered before this command existed.
/// </para>
/// <para>
/// <b>TRANSACTIONS ARE DELIBERATELY NOT TOUCHED.</b> A pool's catch-up mark is a
/// real <c>IsAdjustment</c> row that genuinely moved the account's balance to
/// the figure the exchange was actually showing, and the user may have
/// reconciled a statement against it. Silently reversing it here would rewrite
/// balance history to undo a correction that was true. So: deleting a pool
/// removes the POOL's own rows and nothing else. If the mark itself was wrong,
/// delete that transaction from the transactions page — with the pool gone, the
/// <c>pools.delete_reprices_units</c> guard no longer stands in the way. (The
/// reported case has no such row at all: a zero-delta mark is skipped, not
/// written, and a seed carries no cash leg by design.)
/// </para>
/// <para>
/// Cascade note: <c>pool_participants</c> and <c>pool_unit_events</c> are both
/// <c>ON DELETE CASCADE</c> against <c>pools</c>, so the database would clear
/// them anyway. The handler removes them explicitly regardless — it keeps the
/// intent in the Application layer where it can be read and tested, rather than
/// leaving "and the children go too" as an unstated property of a migration.
/// </para>
/// </summary>
/// <param name="Id">The pool to remove. Archived or not.</param>
public sealed record DeletePoolCommand(Guid Id) : ICommand;
