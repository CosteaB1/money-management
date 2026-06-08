'use client';

import { format } from 'date-fns';
import { ro } from 'date-fns/locale';
import { TrendingUp } from 'lucide-react';
import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  type TooltipProps,
  XAxis,
  YAxis,
} from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { useNetWorthTrend } from '@/src/lib/api/dashboard';
import { formatMoney } from '@/src/lib/utils/currency';

interface ChartPoint {
  month: string;
  netWorthMdl: number;
  missingFxRate: boolean;
}

const compactMdlFormatter = new Intl.NumberFormat('en-MD', {
  notation: 'compact',
  maximumFractionDigits: 1,
});

// Exported for unit testing — Recharts tooltips don't render through jsdom's
// layout-less DOM, so the content component is verified directly.
export function formatMonthLabel(monthIso: string): string {
  const [yearStr, monthStr] = monthIso.split('-');
  const y = Number(yearStr);
  const m = Number(monthStr);
  if (Number.isNaN(y) || Number.isNaN(m)) return monthIso;
  return format(new Date(y, m - 1, 1), 'MMM', { locale: ro });
}

export function NetWorthTrendTooltip({ active, payload }: TooltipProps<number, string>) {
  if (!active || !payload || payload.length === 0) return null;
  const point = payload[0]?.payload as ChartPoint | undefined;
  if (!point) return null;
  return (
    <div
      className="rounded-md border bg-popover px-3 py-2 text-xs text-popover-foreground shadow-sm"
      role="tooltip"
    >
      <p className="font-medium">{formatMonthLabel(point.month)}</p>
      <p className="tabular-nums">{formatMoney(point.netWorthMdl, 'MDL')}</p>
      {point.missingFxRate && (
        <p className="mt-1 text-amber-600 dark:text-amber-400">FX rates missing</p>
      )}
    </div>
  );
}

export function NetWorthTrendChart() {
  const { data, isLoading, isError } = useNetWorthTrend(6);

  const points: ChartPoint[] = data ?? [];
  const anyMissingFx = points.some((p) => p.missingFxRate);

  return (
    <Card data-testid="net-worth-trend-card" className="h-full">
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center justify-between text-sm font-medium text-muted-foreground">
          <span>Net worth — last 6 months</span>
          <TrendingUp className="h-4 w-4" aria-hidden />
        </CardTitle>
      </CardHeader>
      <CardContent>
        {isError ? (
          <p className="text-sm text-muted-foreground" data-testid="net-worth-trend-error">
            Failed to load net worth trend.
          </p>
        ) : isLoading || !data ? (
          <div
            className="h-72 w-full animate-pulse rounded bg-muted"
            role="status"
            aria-label="Loading"
            data-testid="net-worth-trend-loading"
          />
        ) : points.length === 0 ? (
          <p className="text-sm text-muted-foreground" data-testid="net-worth-trend-empty">
            No data yet.
          </p>
        ) : (
          <>
            <div
              className="h-72 w-full"
              role="img"
              aria-label="Net worth in MDL over the last 6 months"
              data-testid="net-worth-trend-chart"
            >
              {/* Screen-reader fallback: enumerates the same data points the
                  chart paints so assistive tech (and component tests, which
                  run inside jsdom where Recharts cannot measure its container)
                  can read out the underlying series. */}
              <ul className="sr-only" data-testid="net-worth-trend-points">
                {points.map((p) => (
                  <li key={p.month} data-testid="net-worth-trend-point">
                    {formatMonthLabel(p.month)}: {formatMoney(p.netWorthMdl, 'MDL')}
                  </li>
                ))}
              </ul>
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={points} margin={{ top: 8, right: 16, left: 0, bottom: 0 }}>
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
                    content={<NetWorthTrendTooltip />}
                    cursor={{ stroke: 'var(--color-border)', strokeWidth: 1 }}
                  />
                  <Line
                    type="monotone"
                    dataKey="netWorthMdl"
                    stroke="var(--color-chart-1)"
                    strokeWidth={2}
                    dot={{ r: 3, fill: 'var(--color-chart-1)' }}
                    activeDot={{ r: 5 }}
                    isAnimationActive={false}
                  />
                </LineChart>
              </ResponsiveContainer>
            </div>
            {anyMissingFx && (
              <p
                className="mt-2 text-xs text-amber-600 dark:text-amber-400"
                data-testid="net-worth-trend-missing-fx"
              >
                Some accounts were missing FX rates on at least one month — values may be
                incomplete.
              </p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
