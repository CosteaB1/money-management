'use client';

import { PieChart } from 'lucide-react';
import Link from 'next/link';
import { Badge } from '@/src/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { useBudgets } from '@/src/lib/api/budgets';
import { cn } from '@/src/lib/utils/cn';
import { formatMoney } from '@/src/lib/utils/currency';
import type { BudgetDto, BudgetStatus } from '@/src/types/api';

const PROGRESS_CAP = 1.2;
const TOP_N = 5;

const STATUS_BAR: Record<BudgetStatus, string> = {
  OnTrack: 'bg-emerald-500',
  Warning: 'bg-amber-500',
  Over: 'bg-rose-500',
};

const STATUS_BADGE: Record<
  BudgetStatus,
  { variant: 'success' | 'warning' | 'destructive'; label: string }
> = {
  OnTrack: { variant: 'success', label: 'On track' },
  Warning: { variant: 'warning', label: 'Warning' },
  Over: { variant: 'destructive', label: 'Over' },
};

function spendRatio(budget: BudgetDto): number {
  if (budget.monthlyLimit <= 0) return 0;
  return budget.spent / budget.monthlyLimit;
}

export function BudgetProgress() {
  const { data, isLoading, isError } = useBudgets();

  // Sort by spend percentage descending so the rows in most danger of
  // (or already) blowing the cap surface first — much more useful for a
  // glance at the dashboard than the highest-spend rows by absolute MDL.
  const top = data ? [...data].sort((a, b) => spendRatio(b) - spendRatio(a)).slice(0, TOP_N) : [];

  return (
    <Card data-testid="budget-progress-card" className="h-full">
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center justify-between text-sm font-medium text-muted-foreground">
          <span>Budgets this month</span>
          <PieChart className="h-4 w-4" aria-hidden />
        </CardTitle>
      </CardHeader>
      <CardContent>
        {isError ? (
          <p className="text-sm text-muted-foreground" data-testid="budget-progress-error">
            Failed to load budgets.
          </p>
        ) : isLoading || !data ? (
          <BudgetProgressSkeleton />
        ) : top.length === 0 ? (
          <p className="text-sm text-muted-foreground" data-testid="budget-progress-empty">
            No budgets configured.{' '}
            <Link href="/budgets" className="underline underline-offset-2">
              Add one in /budgets
            </Link>{' '}
            to start tracking.
          </p>
        ) : (
          <ul className="space-y-3" data-testid="budget-progress-list">
            {top.map((b) => (
              <BudgetProgressRow key={b.id} budget={b} />
            ))}
          </ul>
        )}

        {data && data.length > 0 && (
          <p className="mt-4 text-xs">
            <Link
              href="/budgets"
              className="text-muted-foreground underline-offset-2 hover:underline"
              data-testid="budget-progress-view-all"
            >
              View all →
            </Link>
          </p>
        )}
      </CardContent>
    </Card>
  );
}

function BudgetProgressRow({ budget }: { budget: BudgetDto }) {
  const ratio = spendRatio(budget);
  const cappedRatio = Math.min(Math.max(ratio, 0), PROGRESS_CAP);
  const badge = STATUS_BADGE[budget.status];

  return (
    <li className="space-y-1" data-testid="budget-progress-row" data-status={budget.status}>
      <div className="flex items-center justify-between text-sm">
        <span className="truncate font-medium" title={budget.categoryName}>
          {budget.categoryName}
        </span>
        <Badge variant={badge.variant} data-testid="budget-progress-status-pill">
          {badge.label}
        </Badge>
      </div>
      <div
        className="h-1.5 w-full overflow-hidden rounded-full bg-muted"
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(Math.min(ratio, 1) * 100)}
        aria-label={`${budget.categoryName} spend ${Math.round(ratio * 100)} percent of limit`}
      >
        <div
          data-testid="budget-progress-bar"
          data-status={budget.status}
          className={cn('h-full transition-[width]', STATUS_BAR[budget.status])}
          style={{ width: `${cappedRatio * 100}%` }}
        />
      </div>
      <div className="flex items-center justify-between text-xs text-muted-foreground tabular-nums">
        <span>{formatMoney(budget.spent, 'MDL')}</span>
        <span>{formatMoney(budget.monthlyLimit, 'MDL')}</span>
      </div>
    </li>
  );
}

const SKELETON_ROW_IDS = ['b1', 'b2', 'b3'] as const;

function BudgetProgressSkeleton() {
  return (
    <div className="space-y-3" role="status" aria-label="Loading">
      {SKELETON_ROW_IDS.map((id) => (
        <div key={id} className="space-y-2">
          <div className="h-3 w-32 animate-pulse rounded bg-muted" />
          <div className="h-1.5 w-full animate-pulse rounded bg-muted" />
        </div>
      ))}
    </div>
  );
}
