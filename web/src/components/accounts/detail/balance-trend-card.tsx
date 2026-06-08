'use client';

import { useMemo, useState } from 'react';
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
import { Label } from '@/src/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/src/components/ui/select';
import { useBalanceOverTime } from '@/src/lib/api/reports';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate, toIsoDateString } from '@/src/lib/utils/date';
import type { AccountDetailDto, BalanceOverTimeInterval } from '@/src/types/api';

const compactFormatter = new Intl.NumberFormat('en-MD', {
  notation: 'compact',
  maximumFractionDigits: 1,
});

/** Window length implied by the chosen interval. Tweak together. */
function windowFor(interval: BalanceOverTimeInterval): { from: string; to: string } {
  const today = new Date();
  const from = new Date(today);
  switch (interval) {
    case 'Daily':
      from.setDate(from.getDate() - 30);
      break;
    case 'Weekly':
      from.setMonth(from.getMonth() - 3);
      break;
    case 'Monthly':
      from.setMonth(from.getMonth() - 6);
      break;
  }
  return { from: toIsoDateString(from), to: toIsoDateString(today) };
}

// Exported for unit testing — Recharts tooltips don't render through jsdom's
// layout-less DOM, so the content component is verified directly.
export function BalanceTrendTooltip({
  active,
  payload,
  currency,
  showMdl,
}: TooltipProps<number, string> & { currency: string; showMdl: boolean }) {
  if (!active || !payload || payload.length === 0) return null;
  const point = payload[0]?.payload as
    | { asOf: string; balance: number; balanceMdl: number | null; missingFxRate: boolean }
    | undefined;
  if (!point) return null;
  return (
    <div
      className="rounded-md border bg-popover px-3 py-2 text-xs text-popover-foreground shadow-sm"
      role="tooltip"
    >
      <p className="font-medium">{formatShortDate(point.asOf)}</p>
      <p className="tabular-nums">{formatMoney(point.balance, currency)}</p>
      {showMdl && point.balanceMdl !== null && (
        <p className="tabular-nums text-muted-foreground">
          ≈ {formatMoney(point.balanceMdl, 'MDL')}
        </p>
      )}
      {point.missingFxRate && (
        <p className="mt-1 text-amber-600 dark:text-amber-400">FX rate missing</p>
      )}
    </div>
  );
}

interface Props {
  account: AccountDetailDto;
}

/**
 * Per-account balance-over-time chart. Same Recharts pattern as the
 * reports-page chart, but pre-bound to one accountId and surfaced as a
 * card so it composes with the other detail-page sections.
 *
 * The trailing window scales with the interval: Daily → 30 days,
 * Weekly → 3 months, Monthly → 6 months. Switching the interval
 * re-derives the window so users don't have to fiddle with date
 * pickers just to change the granularity.
 */
export function BalanceTrendCard({ account }: Props) {
  const [interval, setInterval] = useState<BalanceOverTimeInterval>('Monthly');
  const range = useMemo(() => windowFor(interval), [interval]);

  const currency = account.currency;
  const showMdl = currency !== 'MDL';

  const { data, isLoading, isError } = useBalanceOverTime({
    accountId: account.id,
    from: range.from,
    to: range.to,
    interval,
  });

  const points = useMemo(() => data ?? [], [data]);
  const anyMissingFx = points.some((p) => p.missingFxRate);

  return (
    <Card data-testid="balance-trend-card">
      <CardHeader className="flex flex-row items-center justify-between gap-3 pb-3">
        <CardTitle className="text-sm font-medium text-muted-foreground">
          Balance over time
        </CardTitle>
        <div className="min-w-[140px] space-y-1.5">
          <Label htmlFor="trend-interval" className="sr-only">
            Interval
          </Label>
          <Select value={interval} onValueChange={(v) => setInterval(v as BalanceOverTimeInterval)}>
            <SelectTrigger
              id="trend-interval"
              data-testid="trend-interval"
              className="h-8 w-32 text-xs"
            >
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="Daily">Daily</SelectItem>
              <SelectItem value="Weekly">Weekly</SelectItem>
              <SelectItem value="Monthly">Monthly</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </CardHeader>
      <CardContent>
        {isError ? (
          <p className="text-sm text-muted-foreground" data-testid="balance-trend-card-error">
            Failed to load balance history.
          </p>
        ) : isLoading || !data ? (
          <div
            className="h-72 w-full animate-pulse rounded bg-muted"
            role="status"
            aria-label="Loading"
            data-testid="balance-trend-card-loading"
          />
        ) : points.length === 0 ? (
          <p className="text-sm text-muted-foreground" data-testid="balance-trend-card-empty">
            No data in this range yet.
          </p>
        ) : (
          <>
            <div
              className="h-72 w-full"
              role="img"
              aria-label={`Balance history for ${account.name}`}
              data-testid="balance-trend-chart"
              data-show-mdl={showMdl ? 'true' : 'false'}
            >
              <ul className="sr-only" data-testid="balance-trend-points">
                {points.map((p) => (
                  <li key={p.asOf} data-testid="balance-trend-point">
                    {formatShortDate(p.asOf)}: {formatMoney(p.balance, currency)}
                    {showMdl && p.balanceMdl !== null
                      ? ` (≈ ${formatMoney(p.balanceMdl, 'MDL')})`
                      : ''}
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
                    dataKey="asOf"
                    tickFormatter={(v: string) => formatShortDate(v)}
                    stroke="var(--color-muted-foreground)"
                    fontSize={12}
                    tickLine={false}
                    axisLine={false}
                  />
                  <YAxis
                    tickFormatter={(v: number) => `${compactFormatter.format(v)} ${currency}`}
                    stroke="var(--color-muted-foreground)"
                    fontSize={12}
                    tickLine={false}
                    axisLine={false}
                    width={90}
                  />
                  <Tooltip
                    content={<BalanceTrendTooltip currency={currency} showMdl={showMdl} />}
                    cursor={{ stroke: 'var(--color-border)', strokeWidth: 1 }}
                  />
                  <Line
                    type="monotone"
                    dataKey="balance"
                    name={`Balance (${currency})`}
                    stroke="var(--color-chart-1)"
                    strokeWidth={2}
                    dot={{ r: 2, fill: 'var(--color-chart-1)' }}
                    activeDot={{ r: 4 }}
                    isAnimationActive={false}
                  />
                </LineChart>
              </ResponsiveContainer>
            </div>
            {anyMissingFx && (
              <output
                className="mt-3 block text-xs text-amber-600 dark:text-amber-400"
                data-testid="balance-trend-missing-fx"
              >
                Some points are missing FX rates — MDL equivalents may be incomplete.
              </output>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
