import { describe, expect, it } from 'vitest';
import {
  formatFractionPercent,
  formatNav,
  formatSharePercent,
  formatUnits,
  MARK_CRITICAL_DAYS,
  MARK_STALE_DAYS,
  markStaleness,
  POOL_EVENT_LABEL,
} from '@/src/lib/utils/pool';

describe('formatSharePercent', () => {
  it('renders two decimals', () => {
    expect(formatSharePercent(20)).toBe('20.00%');
    expect(formatSharePercent(26.5)).toBe('26.50%');
    expect(formatSharePercent(0)).toBe('0.00%');
  });

  it('never rounds a real sliver of ownership down to a flat zero', () => {
    // "You own nothing" and "you own a sliver" are materially different
    // statements in a pool.
    expect(formatSharePercent(0.001)).toBe('<0.01%');
    expect(formatSharePercent(-0.001)).toBe('>-0.01%');
  });

  it('rounds a genuinely small but representable share normally', () => {
    expect(formatSharePercent(0.006)).toBe('0.01%');
  });
});

describe('formatFractionPercent', () => {
  it('scales a [0,1] fraction into a percentage', () => {
    expect(formatFractionPercent(0.2)).toBe('20.00%');
    expect(formatFractionPercent(1)).toBe('100.00%');
  });
});

describe('formatNav', () => {
  it('keeps six decimals so a near-1.0 price still shows its drift', () => {
    expect(formatNav(1.1000017293)).toBe('1.100002');
    expect(formatNav(1)).toBe('1.000000');
  });
});

describe('formatUnits', () => {
  it('renders four decimals with grouping', () => {
    expect(formatUnits(909.091052)).toBe('909.0911');
    expect(formatUnits(2525.5687569231)).toBe('2,525.5688');
  });
});

describe('POOL_EVENT_LABEL', () => {
  it('never leaks a backend enum name to the screen', () => {
    expect(POOL_EVENT_LABEL.Seed).toBe('Opening stake');
    expect(POOL_EVENT_LABEL.Subscription).toBe('Money in');
    expect(POOL_EVENT_LABEL.Redemption).toBe('Money out');
    expect(POOL_EVENT_LABEL.Distribution).toBe('Profit payout');
    expect(POOL_EVENT_LABEL.CostShare).toBe('Cost share');
    expect(POOL_EVENT_LABEL.CostRecovery).toBe('Cost recovered');
  });
});

describe('markStaleness', () => {
  it('treats a never-valued pool as the worst case, not the best', () => {
    // A pool with no mark has priced every share it ever issued against a
    // balance nobody confirmed — that is not "fine because there is nothing
    // to report".
    expect(markStaleness(null)).toBe('never');
  });

  it('stays silent inside the grace window', () => {
    expect(markStaleness(0)).toBe('fresh');
    expect(markStaleness(MARK_STALE_DAYS - 1)).toBe('fresh');
  });

  it('turns amber at a week and red at a month, inclusive of the boundary', () => {
    expect(markStaleness(MARK_STALE_DAYS)).toBe('stale');
    expect(markStaleness(MARK_CRITICAL_DAYS - 1)).toBe('stale');
    expect(markStaleness(MARK_CRITICAL_DAYS)).toBe('critical');
    expect(markStaleness(120)).toBe('critical');
  });
});
