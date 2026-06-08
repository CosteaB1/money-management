'use client';

import { format } from 'date-fns';
import { ro } from 'date-fns/locale';
import { useMemo, useState } from 'react';
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
import { DateRangePicker } from '@/src/components/reports/date-range-picker';
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
import { formatMonthYear } from '@/src/lib/utils/date';

const compactMdlFormatter = new Intl.NumberFormat('en-MD', {
  notation: 'compact',
  maximumFractionDigits: 1,
});

const percentFormatter = new Intl.NumberFormat('en-MD', {
  style: 'percent',
  maximumFractionDigits: 0,
});

/** "YYYY-MM" of the month `n` months before `from`. `n=0` returns `from`. */
function shiftMonth(monthIso: string, deltaMonths: number): string {
  const [yearStr, monthStr] = monthIso.split('-');
  const year = Number(yearStr);
  const month = Number(monthStr);
  if (Number.isNaN(year) || Number.isNaN(month)) return monthIso;
  const d = new Date(year, month - 1 + deltaMonths, 1);
  return format(d, 'yyyy-MM');
}

function currentMonthIso(): string {
  return format(new Date(), 'yyyy-MM');
}

function defaultFromTrailing12(): { from: string; to: string } {
  const to = currentMonthIso();
  return { from: shiftMonth(to, -11), to };
}

function formatMonthLabel(monthIso: string): string {
  const [yearStr, monthStr] = monthIso.split('-');
  const y = Number(yearStr);
  const m = Number(monthStr);
  if (Number.isNaN(y) || Number.isNaN(m)) return monthIso;
  return format(new Date(y, m - 1, 1), 'MMM yy', { locale: ro });
}

interface ChartPoint {
  month: string;
  income: number;
  expense: number;
  net: number;
  savingsRate: number;
  transactionCount: number;
  missingFxRate: boolean;
}

// Exported for unit testing — Recharts tooltips don't render through jsdom.
export function MonthlySummaryTooltip({ active, payload, label }: TooltipProps<number, string>) {
  if (!active || !payload || payload.length === 0) return null;
  const point = payload[0]?.payload as ChartPoint | undefined;
  if (!point) return null;
  return (
    <div
      className="rounded-md border bg-popover px-3 py-2 text-xs text-popover-foreground shadow-sm"
      role="tooltip"
    >
      <p className="font-medium">{formatMonthYear(label as string)}</p>
      <p className="tabular-nums text-emerald-600 dark:text-emerald-400">
        Income: {formatMoney(point.income, 'MDL')}
      </p>
      <p className="tabular-nums text-red-600 dark:text-red-400">
        Expense: {formatMoney(point.expense, 'MDL')}
      </p>
      <p className="tabular-nums">Net: {formatMoney(point.net, 'MDL')}</p>
      {point.missingFxRate && (
        <p className="mt-1 text-amber-600 dark:text-amber-400">FX rates missing</p>
      )}
    </div>
  );
}

export function MonthlySummarySection() {
  const [range, setRange] = useState<{ from: string; to: string }>(defaultFromTrailing12);

  const { data, isLoading, isError } = useMonthlySummary(range);

  const points: ChartPoint[] = useMemo(() => data ?? [], [data]);
  const anyMissingFx = points.some((p) => p.missingFxRate);

  return (
    <Card data-testid="monthly-summary-section" className="h-full">
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-medium text-muted-foreground">Monthly summary</CardTitle>
        <div className="pt-3">
          <DateRangePicker
            from={range.from}
            to={range.to}
            onChange={setRange}
            resolution="month"
            idPrefix="monthly-summary"
            testIdPrefix="monthly-summary"
          />
        </div>
      </CardHeader>
      <CardContent>
        {isError ? (
          <p className="text-sm text-muted-foreground" data-testid="monthly-summary-section-error">
            Failed to load monthly summary.
          </p>
        ) : isLoading || !data ? (
          <div
            className="h-72 w-full animate-pulse rounded bg-muted"
            role="status"
            aria-label="Loading"
            data-testid="monthly-summary-section-loading"
          />
        ) : points.length === 0 ? (
          <p className="text-sm text-muted-foreground" data-testid="monthly-summary-section-empty">
            No data in this range yet.
          </p>
        ) : (
          <>
            <div
              className="h-72 w-full"
              role="img"
              aria-label="Income vs expense by month"
              data-testid="monthly-summary-section-chart"
            >
              <ul className="sr-only" data-testid="monthly-summary-points">
                {points.map((p) => (
                  <li key={p.month} data-testid="monthly-summary-point">
                    {formatMonthLabel(p.month)}: income {formatMoney(p.income, 'MDL')}, expense{' '}
                    {formatMoney(p.expense, 'MDL')}, net {formatMoney(p.net, 'MDL')}
                  </li>
                ))}
              </ul>
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={points} margin={{ top: 8, right: 16, left: 0, bottom: 0 }}>
                  <CartesianGrid
                    strokeDasharray="3 3"
                    stroke="var(--color-border)"
                    vertical={false}
                  />
                  <XAxis
                    dataKey="month"
                    tickFormatter={formatMonthLabel}
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
                  <Tooltip
                    content={<MonthlySummaryTooltip />}
                    cursor={{ fill: 'var(--color-muted)', opacity: 0.3 }}
                  />
                  <Legend wrapperStyle={{ fontSize: 12 }} />
                  <Bar
                    dataKey="income"
                    name="Income"
                    fill="var(--color-chart-2)"
                    radius={[2, 2, 0, 0]}
                    isAnimationActive={false}
                  />
                  <Bar
                    dataKey="expense"
                    name="Expense"
                    fill="var(--color-chart-1)"
                    radius={[2, 2, 0, 0]}
                    isAnimationActive={false}
                  />
                </BarChart>
              </ResponsiveContainer>
            </div>

            <div className="mt-6 overflow-hidden rounded-lg border">
              <Table data-testid="monthly-summary-table">
                <TableHeader>
                  <TableRow>
                    <TableHead>Month</TableHead>
                    <TableHead className="text-right">Income</TableHead>
                    <TableHead className="text-right">Expense</TableHead>
                    <TableHead className="text-right">Net</TableHead>
                    <TableHead className="text-right">Savings</TableHead>
                    <TableHead className="text-right">Tx</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {points.map((p) => (
                    <TableRow key={p.month} data-testid="monthly-summary-row">
                      <TableCell className="font-medium">{formatMonthYear(p.month)}</TableCell>
                      <TableCell className="text-right tabular-nums text-emerald-600 dark:text-emerald-400">
                        {formatMoney(p.income, 'MDL')}
                      </TableCell>
                      <TableCell className="text-right tabular-nums text-red-600 dark:text-red-400">
                        {formatMoney(p.expense, 'MDL')}
                      </TableCell>
                      <TableCell
                        className={`text-right font-medium tabular-nums ${
                          p.net > 0
                            ? 'text-emerald-600 dark:text-emerald-400'
                            : p.net < 0
                              ? 'text-red-600 dark:text-red-400'
                              : ''
                        }`}
                      >
                        {formatMoney(p.net, 'MDL')}
                      </TableCell>
                      <TableCell className="text-right tabular-nums text-muted-foreground">
                        {p.income === 0 ? '—' : percentFormatter.format(p.savingsRate)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums text-muted-foreground">
                        {p.transactionCount}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>

            {anyMissingFx && (
              <p
                className="mt-3 text-xs text-amber-600 dark:text-amber-400"
                data-testid="monthly-summary-section-missing-fx"
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
