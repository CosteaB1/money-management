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
import { DateRangePicker } from '@/src/components/reports/date-range-picker';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { Label } from '@/src/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/src/components/ui/select';
import { useAccounts } from '@/src/lib/api/accounts';
import { useBalanceOverTime } from '@/src/lib/api/reports';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate, toIsoDateString } from '@/src/lib/utils/date';
import type { AccountDto, BalanceOverTimeInterval, BalanceOverTimePoint } from '@/src/types/api';

const compactFormatter = new Intl.NumberFormat('en-MD', {
  notation: 'compact',
  maximumFractionDigits: 1,
});

function defaultTrailingSixMonths(): { from: string; to: string } {
  const today = new Date();
  const from = new Date(today);
  from.setMonth(from.getMonth() - 6);
  return { from: toIsoDateString(from), to: toIsoDateString(today) };
}

// Exported for unit testing — Recharts tooltips don't render through jsdom.
export function BalanceOverTimeTooltip({
  active,
  payload,
  currency,
  showMdl,
}: TooltipProps<number, string> & { currency: string; showMdl: boolean }) {
  if (!active || !payload || payload.length === 0) return null;
  const point = payload[0]?.payload as BalanceOverTimePoint | undefined;
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

export function BalanceOverTimeSection() {
  const accountsQuery = useAccounts(false);
  const accounts = accountsQuery.data ?? [];

  const [accountId, setAccountId] = useState<string>('');
  const [range, setRange] = useState<{ from: string; to: string }>(defaultTrailingSixMonths);
  const [interval, setInterval] = useState<BalanceOverTimeInterval>('Monthly');

  const selectedAccount: AccountDto | undefined = useMemo(
    () => accounts.find((a) => a.id === accountId),
    [accounts, accountId],
  );

  const currency = selectedAccount?.currency ?? 'MDL';
  const showMdl = currency !== 'MDL';

  const { data, isLoading, isError } = useBalanceOverTime({
    accountId,
    from: range.from,
    to: range.to,
    interval,
  });

  const points: BalanceOverTimePoint[] = useMemo(() => data ?? [], [data]);
  const anyMissingFx = points.some((p) => p.missingFxRate);

  return (
    <Card data-testid="balance-over-time-section" className="h-full">
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-medium text-muted-foreground">
          Balance over time
        </CardTitle>
        <div className="flex flex-wrap items-end gap-4 pt-3">
          <div className="min-w-[200px] space-y-1.5">
            <Label htmlFor="balance-account" className="text-xs text-muted-foreground">
              Account
            </Label>
            <Select value={accountId} onValueChange={setAccountId}>
              <SelectTrigger id="balance-account" data-testid="balance-account">
                <SelectValue placeholder="Select account" />
              </SelectTrigger>
              <SelectContent>
                {accounts.map((a) => (
                  <SelectItem key={a.id} value={a.id}>
                    {a.name} ({a.currency})
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <DateRangePicker
            from={range.from}
            to={range.to}
            onChange={setRange}
            idPrefix="balance-range"
            testIdPrefix="balance-range"
          />
          <div className="min-w-[140px] space-y-1.5">
            <Label htmlFor="balance-interval" className="text-xs text-muted-foreground">
              Interval
            </Label>
            <Select
              value={interval}
              onValueChange={(v) => setInterval(v as BalanceOverTimeInterval)}
            >
              <SelectTrigger id="balance-interval" data-testid="balance-interval">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Daily">Daily</SelectItem>
                <SelectItem value="Weekly">Weekly</SelectItem>
                <SelectItem value="Monthly">Monthly</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
      </CardHeader>
      <CardContent>
        {!accountId ? (
          <p
            className="text-sm text-muted-foreground"
            data-testid="balance-over-time-section-empty-account"
          >
            Pick an account to see its balance history.
          </p>
        ) : isError ? (
          <p
            className="text-sm text-muted-foreground"
            data-testid="balance-over-time-section-error"
          >
            Failed to load balance history.
          </p>
        ) : isLoading || !data ? (
          <div
            className="h-72 w-full animate-pulse rounded bg-muted"
            role="status"
            aria-label="Loading"
            data-testid="balance-over-time-section-loading"
          />
        ) : points.length === 0 ? (
          <p
            className="text-sm text-muted-foreground"
            data-testid="balance-over-time-section-empty"
          >
            No data in this range yet.
          </p>
        ) : (
          <>
            <div
              className="h-72 w-full"
              role="img"
              aria-label={`Balance history for ${selectedAccount?.name ?? 'account'}`}
              data-testid="balance-over-time-chart"
              data-show-mdl={showMdl ? 'true' : 'false'}
            >
              <ul className="sr-only" data-testid="balance-over-time-points">
                {points.map((p) => (
                  <li key={p.asOf} data-testid="balance-over-time-point">
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
                    content={<BalanceOverTimeTooltip currency={currency} showMdl={showMdl} />}
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
                    data-testid="balance-native-line"
                  />
                </LineChart>
              </ResponsiveContainer>
            </div>
            {anyMissingFx && (
              <p
                className="mt-3 text-xs text-amber-600 dark:text-amber-400"
                data-testid="balance-over-time-missing-fx"
              >
                Some points are missing FX rates — MDL equivalents may be incomplete.
              </p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
