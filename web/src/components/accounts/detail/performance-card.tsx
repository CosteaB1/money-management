'use client';

import { useState } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { Tabs, TabsList, TabsTrigger } from '@/src/components/ui/tabs';
import { cn } from '@/src/lib/utils/cn';
import { formatMoney } from '@/src/lib/utils/currency';
import type { AccountActivityTotalsDto, AccountDetailDto } from '@/src/types/api';

type Window = 'YTD' | 'AllTime';

interface Props {
  account: AccountDetailDto;
}

function pnlClass(amount: number): string {
  if (amount > 0) return 'text-emerald-500';
  if (amount < 0) return 'text-rose-500';
  return 'text-muted-foreground';
}

interface CellProps {
  label: string;
  value: string;
  subtitle: string;
  testId: string;
  valueClassName?: string;
}

function Cell({ label, value, subtitle, testId, valueClassName }: CellProps) {
  return (
    <div className="space-y-1" data-testid={testId}>
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p>
      <p className={cn('text-xl font-semibold tabular-nums', valueClassName)}>{value}</p>
      <p className="text-xs text-muted-foreground">{subtitle}</p>
    </div>
  );
}

/**
 * Per-account Performance card. Three KPIs in a row:
 * +Contributions → −Withdrawals → ±Net P&L → Current value.
 *
 * Totals are MDL-only on the DTO (the backend collapses native to MDL
 * via FX), so the card always speaks MDL except for the Current value
 * which includes the native amount alongside the MDL conversion when
 * the account currency != MDL.
 *
 * The YTD / All-time toggle only swaps the source of the activity
 * totals — Current is NOT window-scoped and stays put.
 */
export function PerformanceCard({ account }: Props) {
  const [window, setWindow] = useState<Window>('YTD');
  const totals: AccountActivityTotalsDto = window === 'YTD' ? account.yearToDate : account.allTime;

  const showMdl = account.currency !== 'MDL';

  return (
    <Card data-testid="performance-card" data-window={window}>
      <CardHeader className="flex flex-row items-center justify-between gap-3 pb-3">
        <CardTitle className="text-sm font-medium text-muted-foreground">Performance</CardTitle>
        <Tabs
          value={window}
          onValueChange={(v) => setWindow(v as Window)}
          aria-label="Performance window"
        >
          <TabsList className="h-8">
            <TabsTrigger value="YTD" className="px-2.5 py-1 text-xs" data-testid="perf-window-ytd">
              YTD
            </TabsTrigger>
            <TabsTrigger
              value="AllTime"
              className="px-2.5 py-1 text-xs"
              data-testid="perf-window-all"
            >
              All-time
            </TabsTrigger>
          </TabsList>
        </Tabs>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid grid-cols-2 gap-4 md:grid-cols-3">
          <Cell
            label="+ Contributions"
            value={formatMoney(totals.contributionsMdl, 'MDL')}
            subtitle={`${totals.contributionCount} ${
              totals.contributionCount === 1 ? 'deposit' : 'deposits'
            }`}
            testId="perf-contributions"
            valueClassName="text-emerald-500"
          />
          <Cell
            label="− Withdrawals"
            value={formatMoney(totals.withdrawalsMdl, 'MDL')}
            subtitle={`${totals.withdrawalCount} ${
              totals.withdrawalCount === 1 ? 'withdrawal' : 'withdrawals'
            }`}
            testId="perf-withdrawals"
            valueClassName="text-rose-500"
          />
          <Cell
            label="Net P&L"
            value={formatMoney(totals.netPnLMdl, 'MDL')}
            subtitle={`${totals.adjustmentCount} ${
              totals.adjustmentCount === 1 ? 'update' : 'updates'
            }`}
            testId="perf-pnl"
            valueClassName={pnlClass(totals.netPnLMdl)}
          />
        </div>

        <div
          className="flex flex-wrap items-baseline gap-2 border-t pt-3 text-sm"
          data-testid="perf-current-line"
        >
          <span className="text-muted-foreground">Current value:</span>
          <span className="font-medium tabular-nums" data-testid="perf-current-mdl">
            {account.balanceMdl !== null ? (
              formatMoney(account.balanceMdl, 'MDL')
            ) : (
              <span title={`No FX rate available for ${account.currency}.`}>—</span>
            )}
          </span>
          {showMdl && (
            <span
              className="text-xs text-muted-foreground tabular-nums"
              data-testid="perf-current-native"
            >
              ({formatMoney(account.balance, account.currency)})
            </span>
          )}
        </div>

        {totals.missingFxRate && (
          <output
            className="block text-xs text-amber-600 dark:text-amber-400"
            data-testid="perf-missing-fx"
          >
            Some rows were missing FX rates — totals may be incomplete.
          </output>
        )}
      </CardContent>
    </Card>
  );
}
