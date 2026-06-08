'use client';

import { format } from 'date-fns';
import { ro } from 'date-fns/locale';
import { useMemo } from 'react';
import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  ResponsiveContainer,
  Tooltip,
  type TooltipProps,
  XAxis,
  YAxis,
} from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/src/components/ui/table';
import { useMonthlySummary } from '@/src/lib/api/reports';
import { formatMoney } from '@/src/lib/utils/currency';
import type { MonthlySummaryReportRow } from '@/src/types/api';

const compactMdlFormatter = new Intl.NumberFormat('en-MD', {
  notation: 'compact',
  maximumFractionDigits: 1,
});

/**
 * Trailing-24-month window in `"YYYY-MM"` form, used to drive the
 * `useMonthlySummary` query. We deliberately match the keys used by the
 * Monthly Summary tab so TanStack Query can share a single cache entry
 * when both tabs request the trailing-24 window — there's no shared
 * default though, so in practice each tab fetches its own window.
 */
function trailing24MonthRange(): { from: string; to: string } {
  const today = new Date();
  const to = format(today, 'yyyy-MM');
  const fromDate = new Date(today.getFullYear(), today.getMonth() - 23, 1);
  const from = format(fromDate, 'yyyy-MM');
  return { from, to };
}

/**
 * Single "month-of-year" row paired across two years.
 * `monthLabel` is locale-formatted ("ian", "feb", …), `priorMonth` and
 * `currentMonth` are the underlying ISO strings (handy in tests).
 */
interface PairedMonth {
  monthIndex: number;
  monthLabel: string;
  priorMonth: string | null;
  currentMonth: string | null;
  priorIncome: number;
  priorExpense: number;
  priorNet: number;
  currentIncome: number;
  currentExpense: number;
  currentNet: number;
}

/**
 * Splits a trailing-24-month series into the last 12 (current) and the
 * preceding 12 (prior), then pairs them by month-of-year.
 *
 * Exported for unit testing.
 */
export function buildYoyPairs(rows: MonthlySummaryReportRow[]): PairedMonth[] {
  // No data → no pairs, so the section can render its empty state rather than
  // a table of twelve all-zero rows.
  if (rows.length === 0) return [];
  const tail = rows.slice(-24);
  const prior = tail.slice(0, Math.max(0, tail.length - 12));
  const current = tail.slice(-12);

  function indexByMonth(series: MonthlySummaryReportRow[]) {
    const map = new Map<number, MonthlySummaryReportRow>();
    for (const r of series) {
      const [, monthStr] = r.month.split('-');
      const idx = Number(monthStr) - 1;
      if (!Number.isNaN(idx)) map.set(idx, r);
    }
    return map;
  }

  const priorByMonth = indexByMonth(prior);
  const currentByMonth = indexByMonth(current);

  // Use the current-year ordering for the axis: walk from the oldest
  // month in `current` through to the most recent, so the chart reads
  // chronologically (Jun '25 → May '26 if the window ends in May).
  const order: number[] = current.map((r) => Number(r.month.split('-')[1]) - 1);
  // Fallback: if `current` is empty, fall back to Jan→Dec.
  const ordering = order.length > 0 ? order : [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

  return ordering.map((monthIndex) => {
    const priorRow = priorByMonth.get(monthIndex);
    const currentRow = currentByMonth.get(monthIndex);
    return {
      monthIndex,
      monthLabel: format(new Date(2000, monthIndex, 1), 'MMM', { locale: ro }),
      priorMonth: priorRow?.month ?? null,
      currentMonth: currentRow?.month ?? null,
      priorIncome: priorRow?.income ?? 0,
      priorExpense: priorRow?.expense ?? 0,
      priorNet: priorRow?.net ?? 0,
      currentIncome: currentRow?.income ?? 0,
      currentExpense: currentRow?.expense ?? 0,
      currentNet: currentRow?.net ?? 0,
    };
  });
}

// Exported for unit testing — Recharts tooltips don't render through jsdom.
export function YoyTooltip({ active, payload, label }: TooltipProps<number, string>) {
  if (!active || !payload || payload.length === 0) return null;
  const point = payload[0]?.payload as PairedMonth | undefined;
  if (!point) return null;
  return (
    <div
      className="rounded-md border bg-popover px-3 py-2 text-xs text-popover-foreground shadow-sm"
      role="tooltip"
    >
      <p className="font-medium">{label}</p>
      <p className="tabular-nums">
        Prior net: <span>{formatMoney(point.priorNet, 'MDL')}</span>
      </p>
      <p className="tabular-nums">
        Current net: <span>{formatMoney(point.currentNet, 'MDL')}</span>
      </p>
    </div>
  );
}

export function YearOverYearSection() {
  // The same range is also used by `MonthlySummarySection` when the user
  // opts into 24-month view, so TanStack Query naturally de-duplicates
  // identical fetches keyed by `{from, to}`.
  const range = useMemo(trailing24MonthRange, []);
  const { data, isLoading, isError } = useMonthlySummary(range);

  const pairs = useMemo(() => buildYoyPairs(data ?? []), [data]);
  const anyMissingFx = (data ?? []).some((r) => r.missingFxRate);

  return (
    <Card data-testid="year-over-year-section" className="h-full">
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-medium text-muted-foreground">
          Year over year — net
        </CardTitle>
      </CardHeader>
      <CardContent>
        {isError ? (
          <p className="text-sm text-muted-foreground" data-testid="year-over-year-section-error">
            Failed to load year-over-year.
          </p>
        ) : isLoading || !data ? (
          <div
            className="h-72 w-full animate-pulse rounded bg-muted"
            role="status"
            aria-label="Loading"
            data-testid="year-over-year-section-loading"
          />
        ) : pairs.length === 0 ? (
          <p className="text-sm text-muted-foreground" data-testid="year-over-year-section-empty">
            No data yet.
          </p>
        ) : (
          <>
            <div
              className="h-72 w-full"
              role="img"
              aria-label="Year over year net comparison"
              data-testid="year-over-year-chart"
            >
              <ul className="sr-only" data-testid="year-over-year-points">
                {pairs.map((p) => (
                  <li
                    key={`${p.monthIndex}-${p.currentMonth ?? 'none'}`}
                    data-testid="year-over-year-point"
                  >
                    {p.monthLabel}: prior {formatMoney(p.priorNet, 'MDL')}, current{' '}
                    {formatMoney(p.currentNet, 'MDL')}
                  </li>
                ))}
              </ul>
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={pairs} margin={{ top: 8, right: 16, left: 0, bottom: 0 }}>
                  <CartesianGrid
                    strokeDasharray="3 3"
                    stroke="var(--color-border)"
                    vertical={false}
                  />
                  <XAxis
                    dataKey="monthLabel"
                    stroke="var(--color-muted-foreground)"
                    fontSize={12}
                    tickLine={false}
                    axisLine={false}
                  />
                  <YAxis
                    tickFormatter={(v: number) => `${compactMdlFormatter.format(v)} MDL`}
                    stroke="var(--color-muted-foreground)"
                    fontSize={12}
                    tickLine={false}
                    axisLine={false}
                    width={80}
                  />
                  <Tooltip content={<YoyTooltip />} />
                  <Legend wrapperStyle={{ fontSize: 12 }} />
                  <Bar
                    dataKey="priorNet"
                    name="Prior 12 mo"
                    fill="var(--color-chart-3)"
                    radius={[2, 2, 0, 0]}
                    isAnimationActive={false}
                  />
                  <Bar
                    dataKey="currentNet"
                    name="Last 12 mo"
                    fill="var(--color-chart-1)"
                    radius={[2, 2, 0, 0]}
                    isAnimationActive={false}
                  />
                </BarChart>
              </ResponsiveContainer>
            </div>

            <div className="mt-6 overflow-hidden rounded-lg border">
              <Table data-testid="year-over-year-table">
                <TableHeader>
                  <TableRow>
                    <TableHead>Month</TableHead>
                    <TableHead className="text-right">Prior net</TableHead>
                    <TableHead className="text-right">Current net</TableHead>
                    <TableHead className="text-right">Δ</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {pairs.map((p) => {
                    const delta = p.currentNet - p.priorNet;
                    const deltaClass =
                      delta > 0
                        ? 'text-emerald-600 dark:text-emerald-400'
                        : delta < 0
                          ? 'text-red-600 dark:text-red-400'
                          : 'text-muted-foreground';
                    return (
                      <TableRow
                        key={`${p.monthIndex}-${p.currentMonth ?? 'none'}`}
                        data-testid="year-over-year-row"
                      >
                        <TableCell className="font-medium">{p.monthLabel}</TableCell>
                        <TableCell className="text-right tabular-nums text-muted-foreground">
                          {formatMoney(p.priorNet, 'MDL')}
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {formatMoney(p.currentNet, 'MDL')}
                        </TableCell>
                        <TableCell className={`text-right font-medium tabular-nums ${deltaClass}`}>
                          {formatMoney(delta, 'MDL')}
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </div>

            {anyMissingFx && (
              <p
                className="mt-3 text-xs text-amber-600 dark:text-amber-400"
                data-testid="year-over-year-missing-fx"
              >
                Some transactions couldn&apos;t be converted to MDL — totals may be incomplete.
              </p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
