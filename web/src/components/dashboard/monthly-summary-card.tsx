'use client';

import { Wallet } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { useDashboardSummary } from '@/src/lib/api/dashboard';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatMonthYear } from '@/src/lib/utils/date';

const percentFormatter = new Intl.NumberFormat('en-MD', {
  style: 'percent',
  maximumFractionDigits: 0,
});

export function MonthlySummaryCard() {
  const { data, isLoading, isError } = useDashboardSummary();

  return (
    <Card data-testid="monthly-summary-card" className="h-full">
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center justify-between text-sm font-medium text-muted-foreground">
          <span className="flex flex-col gap-0.5">
            <span>This month</span>
            {data && (
              <span
                className="text-xs font-normal text-muted-foreground"
                data-testid="monthly-summary-month"
              >
                {formatMonthYear(data.month)}
              </span>
            )}
          </span>
          <Wallet className="h-4 w-4" aria-hidden />
        </CardTitle>
      </CardHeader>
      <CardContent>
        {isError ? (
          <p className="text-sm text-muted-foreground" data-testid="monthly-summary-error">
            Failed to load monthly summary.
          </p>
        ) : isLoading || !data ? (
          <MonthlySummarySkeleton />
        ) : (
          <MonthlySummaryBody data={data} />
        )}
      </CardContent>
    </Card>
  );
}

function MonthlySummaryBody({
  data,
}: {
  data: {
    income: number;
    expense: number;
    net: number;
    savingsRate: number;
    transactionCount: number;
    missingFxRate: boolean;
  };
}) {
  const netClass =
    data.net > 0
      ? 'text-emerald-600 dark:text-emerald-400'
      : data.net < 0
        ? 'text-red-600 dark:text-red-400'
        : 'text-foreground';

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <Stat
          label="Income"
          value={formatMoney(data.income, 'MDL')}
          tone="text-emerald-600 dark:text-emerald-400"
          testId="monthly-summary-income"
        />
        <Stat
          label="Expense"
          value={formatMoney(data.expense, 'MDL')}
          tone="text-red-600 dark:text-red-400"
          testId="monthly-summary-expense"
        />
        <Stat
          label="Net"
          value={formatMoney(data.net, 'MDL')}
          tone={netClass}
          testId="monthly-summary-net"
        />
      </div>

      <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
        <div>
          <p className="text-xs text-muted-foreground">Savings rate</p>
          <p
            className="text-xl font-semibold tabular-nums"
            data-testid="monthly-summary-savings-rate"
          >
            {data.income === 0 ? '—' : percentFormatter.format(data.savingsRate)}
          </p>
        </div>
        <p className="text-xs text-muted-foreground" data-testid="monthly-summary-tx-count">
          {data.transactionCount === 1 ? '1 transaction' : `${data.transactionCount} transactions`}
        </p>
      </div>

      {data.missingFxRate && (
        <p
          className="text-xs text-amber-600 dark:text-amber-400"
          data-testid="monthly-summary-missing-fx"
        >
          Some transactions couldn&apos;t be converted to MDL — totals may be incomplete.
        </p>
      )}
    </div>
  );
}

function Stat({
  label,
  value,
  tone,
  testId,
}: {
  label: string;
  value: string;
  tone: string;
  testId: string;
}) {
  return (
    <div>
      <p className="text-xs text-muted-foreground">{label}</p>
      <p
        className={`text-2xl font-semibold tabular-nums tracking-tight ${tone}`}
        data-testid={testId}
      >
        {value}
      </p>
    </div>
  );
}

function MonthlySummarySkeleton() {
  return (
    <div className="space-y-4" role="status" aria-label="Loading">
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <div className="h-8 w-32 animate-pulse rounded bg-muted" />
        <div className="h-8 w-32 animate-pulse rounded bg-muted" />
        <div className="h-8 w-32 animate-pulse rounded bg-muted" />
      </div>
      <div className="h-6 w-24 animate-pulse rounded bg-muted" />
    </div>
  );
}
