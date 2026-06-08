import { describe, expect, it, vi } from 'vitest';
import {
  formatLongDate,
  formatMonthYear,
  formatShortDate,
  toIsoDateString,
  todayIsoUtc,
} from '@/src/lib/utils/date';

describe('formatShortDate', () => {
  it('formats an ISO string day-first with a worded month', () => {
    expect(formatShortDate('2026-05-01')).toBe('01 May 2026');
  });

  it('accepts a Date instance', () => {
    expect(formatShortDate(new Date(2026, 4, 1))).toBe('01 May 2026');
  });
});

describe('formatLongDate', () => {
  it('formats with the full month name', () => {
    expect(formatLongDate('2026-05-01')).toBe('01 May 2026');
  });

  it('accepts a Date instance', () => {
    expect(formatLongDate(new Date(2026, 11, 25))).toBe('25 December 2026');
  });
});

describe('formatMonthYear', () => {
  it('formats a YYYY-MM string', () => {
    expect(formatMonthYear('2026-05')).toBe('May 2026');
  });

  it('returns the input unchanged when it is not parseable', () => {
    expect(formatMonthYear('not-a-month')).toBe('not-a-month');
  });
});

describe('toIsoDateString', () => {
  it('formats a Date as yyyy-MM-dd', () => {
    expect(toIsoDateString(new Date(2026, 4, 1))).toBe('2026-05-01');
  });
});

describe('todayIsoUtc', () => {
  it('returns the current UTC calendar date as yyyy-MM-dd', () => {
    expect(todayIsoUtc()).toBe(new Date().toISOString().slice(0, 10));
  });

  it('matches the UTC date even when the local date differs at the boundary', () => {
    // Pin "now" to a UTC instant whose local-vs-UTC calendar day can diverge
    // east/west of UTC; the helper must report the UTC day regardless.
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-06-04T23:30:00.000Z'));
    expect(todayIsoUtc()).toBe('2026-06-04');
    vi.useRealTimers();
  });
});
