import type { PoolDetailDto, PoolUnitEventKind } from '@/src/types/api';

/**
 * Formatting and threshold helpers shared by the pool surfaces.
 *
 * The vocabulary rule from the design review lives here: the UI says
 * **"share"** and shows **percentages**. "Units" is fund-accounting jargon and
 * appears in exactly one place — the event ledger, where an audit trail
 * genuinely needs it.
 */

/**
 * A holder's share of the pool, as a percentage string.
 *
 * Two decimals, because the interesting differences between a 26.50% and a
 * 35.76% owner fraction are in the second place. A share that is non-zero but
 * rounds to 0.00 renders as `<0.01%` rather than a flat zero — "you own
 * nothing" is a materially different statement from "you own a sliver".
 */
export function formatSharePercent(percent: number): string {
  if (percent > 0 && percent < 0.005) return '<0.01%';
  if (percent < 0 && percent > -0.005) return '>-0.01%';
  return `${percent.toFixed(2)}%`;
}

/** `0.2650` (a fraction in [0, 1]) → `"26.50%"`. */
export function formatFractionPercent(fraction: number): string {
  return formatSharePercent(fraction * 100);
}

/**
 * NAV per unit, to 6dp.
 *
 * Stored at `numeric(28,12)` precisely because rounding a NAV near 1.0 to the
 * money convention's 2dp quantizes ~0.1% per event and breaks the invariance
 * identity. Six places is enough to read the drift and short of the noise;
 * trailing zeros are kept so a column of NAVs stays aligned.
 */
export function formatNav(nav: number): string {
  return nav.toFixed(6);
}

/**
 * Unit counts, to 4dp — the ledger's audit column only.
 *
 * Deliberately coarser than the stored 12dp: the ledger is there to let the
 * user follow what happened, and 909.0910 tells that story as well as
 * 909.091052000000 does.
 */
export function formatUnits(units: number): string {
  return units.toLocaleString('en-US', {
    minimumFractionDigits: 4,
    maximumFractionDigits: 4,
  });
}

/** Human label for a ledger row's kind. Enum names never reach the screen. */
export const POOL_EVENT_LABEL: Record<PoolUnitEventKind, string> = {
  Seed: 'Opening stake',
  Subscription: 'Money in',
  Redemption: 'Money out',
  Distribution: 'Profit payout',
  CostShare: 'Cost share',
  CostRecovery: 'Cost recovered',
};

/**
 * How stale the pool's last confirmed valuation is.
 *
 *  - `fresh`   → confirmed within the last week; nothing is shown.
 *  - `stale`   → 7 days or more. Amber.
 *  - `critical`→ 30 days or more. Red.
 *  - `never`   → the pool has never been valued at all. Red: every NAV struck
 *                so far was priced off a balance nobody confirmed.
 *
 * This matters more than it looks. Every subscription, redemption and
 * distribution is priced off the account's derived balance; price one against
 * a mark that is weeks old in an account that moves and the units are minted
 * or burned at the wrong number — silently transferring value between the
 * owner and the participants, permanently. The design calls it "the main way
 * this model goes quietly wrong".
 */
export type MarkStaleness = 'never' | 'fresh' | 'stale' | 'critical';

export const MARK_STALE_DAYS = 7;
export const MARK_CRITICAL_DAYS = 30;

export function markStaleness(markAgeDays: number | null): MarkStaleness {
  if (markAgeDays === null) return 'never';
  if (markAgeDays >= MARK_CRITICAL_DAYS) return 'critical';
  if (markAgeDays >= MARK_STALE_DAYS) return 'stale';
  return 'fresh';
}

/**
 * Units/NAV storage scale — `numeric(28,12)`, mirroring
 * `PoolUnitRegister.UnitScale`.
 */
const UNIT_SCALE = 12;

/**
 * Below this many units the pool holds nothing worth pricing. Mirrors
 * `PoolUnitEvent.UnitsDustTolerance` — a units-only `> 0` test would still
 * divide by a residue left behind by a full wind-down.
 */
const UNITS_DUST_TOLERANCE = 0.0000000000005;

/** Rounds to the cent — the scale money is stored and compared at. */
export function roundPoolMoney(amount: number): number {
  return Number(amount.toFixed(2));
}

/** One holder's position under a hypothetical mark. */
export interface PoolPositionPreview {
  /**
   * `units × nav`, deliberately UNROUNDED. Rounding belongs at the edge, in
   * `formatMoney`; rounding here and then subtracting the capital base
   * manufactures a cent that the server's own arithmetic never sees.
   */
  stake: number | null;
  /**
   * `min(stake, max(0, stake − max(0, capitalBase)))` — the high-water rule.
   *
   * The base is floored at zero **where it is consumed, and only there**: the
   * owner seeds units with no cash leg and then redeems, so their reported
   * capital base legitimately goes negative, and fed in raw it would make the
   * distributable exceed the whole stake. Capping at the stake keeps
   * `distributable <= stake` true by construction even for a corrupt position.
   *
   * Null when there is no NAV, exactly like `PoolParticipantDto.distributable`.
   */
  distributable: number | null;
}

/** The pool as it would stand if the user's typed total were the mark. */
export interface PoolValuationPreview {
  /** `poolValueNow − unpaidDistributionCash`. What units are priced against. */
  poolValue: number;
  /** `poolValue / totalUnits` at the storage scale, or null when unpriceable. */
  navPerUnit: number | null;
  /** Keyed by participant id, archived rows included — they hold units. */
  positions: ReadonlyMap<string, PoolPositionPreview>;
}

/**
 * Reprices the whole roster against a total the user has just typed.
 *
 * **Every figure the API sent was struck against the PREVIOUS mark**, and the
 * instant the user types this month's exchange total those figures are wrong —
 * most damagingly `distributable`, which a close sweeps back to ~0 for
 * everybody. Gate the close on the server's copy and the *next* profitable
 * month can never be closed from the UI at all: the dialog reads "nothing is
 * payable" and disables its own button while the pool is visibly up.
 *
 * So the preview is recomputed here, mirroring
 * `PoolUnitRegister.SnapshotAsOf`:
 *
 * ```
 * poolValue = poolValueNow − unpaidDistributionCash   // two-phase close: that
 *                                                     // cash is owed AND still
 *                                                     // sitting in the account
 * nav       = poolValue / totalUnits                  // null when unpriceable
 * stake     = units × nav
 * payable   = min(stake, max(0, stake − max(0, capitalBase)))
 * ```
 *
 * This is a **preview and a gate, not an authority**. The amounts are never
 * sent: the server recomputes them inside the close, off the balance it marks
 * the account to, and an explicit `cash` a cent above someone's freshly-struck
 * distributable is a hard 409. Expect the two to differ by a cent — the server
 * floors the payable amount so its default can never exceed what is owed,
 * while this rounds only for display. Never promise an exact figure from here.
 */
export function previewPoolValuation(
  pool: Pick<PoolDetailDto, 'unpaidDistributionCash' | 'totalUnits' | 'participants'>,
  poolValueNow: number,
): PoolValuationPreview {
  const poolValue = poolValueNow - pool.unpaidDistributionCash;

  // No units => no price. And a pool marked to nothing has no price either:
  // NAV comes out zero or negative and every caller divides by it. Both
  // resolve to null rather than a placeholder, so the callers below have to
  // say "nothing payable" instead of quietly pricing against 1.0.
  let navPerUnit: number | null = null;
  if (Number.isFinite(poolValue) && pool.totalUnits > UNITS_DUST_TOLERANCE) {
    const candidate = Number((poolValue / pool.totalUnits).toFixed(UNIT_SCALE));
    if (candidate > 0) navPerUnit = candidate;
  }

  const positions = new Map<string, PoolPositionPreview>();
  for (const participant of pool.participants) {
    if (navPerUnit === null) {
      positions.set(participant.id, { stake: null, distributable: null });
      continue;
    }
    const stake = participant.units * navPerUnit;
    const distributable = Math.min(
      stake,
      Math.max(0, stake - Math.max(0, participant.capitalBase)),
    );
    positions.set(participant.id, { stake, distributable });
  }

  return { poolValue, navPerUnit, positions };
}
