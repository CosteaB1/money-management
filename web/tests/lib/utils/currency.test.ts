import { describe, expect, it } from 'vitest';
import {
  formatEffectiveRate,
  formatMDL,
  formatMDLCompact,
  formatMoney,
} from '@/src/lib/utils/currency';

describe('formatMDL', () => {
  it('formats a value with the MDL ISO code', () => {
    const out = formatMDL(1234.5);
    expect(out).toMatch(/1\D?234[.,]50/);
    expect(out).toContain('MDL');
  });
});

describe('formatMDLCompact', () => {
  it('formats a large value in compact notation with the MDL code', () => {
    const out = formatMDLCompact(1_200_000);
    expect(out).toContain('MDL');
    // Compact notation collapses to a "1.2M"-style short form.
    expect(out).toMatch(/1[.,]?2/);
  });
});

describe('formatMoney', () => {
  it('formats an arbitrary currency with its ISO code', () => {
    const out = formatMoney(1500, 'USD');
    expect(out).toContain('USD');
    expect(out).toMatch(/1\D?500[.,]00/);
  });

  it('falls back to the MDL formatter when the currency code is invalid', () => {
    const out = formatMoney(42, 'not-a-currency');
    expect(out).toContain('MDL');
    expect(out).toMatch(/42[.,]00/);
  });
});

describe('formatEffectiveRate', () => {
  it('computes the source-per-dest rate label', () => {
    expect(formatEffectiveRate(17163, 1000, 'MDL', 'USD')).toBe('Rate: 17.16 MDL/USD');
  });

  it('returns null when either amount is non-positive', () => {
    expect(formatEffectiveRate(0, 1000, 'MDL', 'USD')).toBeNull();
    expect(formatEffectiveRate(1000, 0, 'MDL', 'USD')).toBeNull();
  });

  it('returns null when an amount is not finite', () => {
    expect(formatEffectiveRate(Number.NaN, 1000, 'MDL', 'USD')).toBeNull();
    expect(formatEffectiveRate(1000, Number.POSITIVE_INFINITY, 'MDL', 'USD')).toBeNull();
  });
});
