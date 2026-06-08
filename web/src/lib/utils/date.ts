import { format, parseISO } from 'date-fns';
import { enGB } from 'date-fns/locale';

export function formatShortDate(date: Date | string): string {
  const d = typeof date === 'string' ? parseISO(date) : date;
  // Day-first with an English-worded month (e.g. 01 May 2026).
  return format(d, 'dd MMM yyyy', { locale: enGB });
}

export function formatLongDate(date: Date | string): string {
  const d = typeof date === 'string' ? parseISO(date) : date;
  return format(d, 'dd MMMM yyyy', { locale: enGB });
}

export function formatMonthYear(month: string): string {
  // month is "YYYY-MM"
  const [yearStr, monthStr] = month.split('-');
  const y = Number(yearStr);
  const m = Number(monthStr);
  if (Number.isNaN(y) || Number.isNaN(m)) return month;
  // English-worded month for chart/label readability (e.g. May 2026).
  return format(new Date(y, m - 1, 1), 'MMM yyyy', { locale: enGB });
}

export function toIsoDateString(date: Date): string {
  return format(date, 'yyyy-MM-dd');
}

/** Today's calendar date in UTC (yyyy-MM-dd). The backend validates "not in
 *  the future" in UTC, so date defaults/validation must match to avoid a
 *  spurious future-date rejection at the local-vs-UTC midnight boundary. */
export function todayIsoUtc(): string {
  return new Date().toISOString().slice(0, 10);
}
