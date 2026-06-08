'use client';

import { Target } from 'lucide-react';
import Link from 'next/link';
import { Badge } from '@/src/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { useGoals } from '@/src/lib/api/goals';
import { cn } from '@/src/lib/utils/cn';
import { formatMoney } from '@/src/lib/utils/currency';
import type { GoalDto, GoalStatus } from '@/src/types/api';

const PROGRESS_CAP = 1.2;
const TOP_N = 3;

const STATUS_BAR: Record<GoalStatus, string> = {
  OnTrack: 'bg-emerald-500',
  AtRisk: 'bg-amber-500',
  Achieved: 'bg-emerald-500',
  Behind: 'bg-rose-500',
};

const STATUS_BADGE: Record<
  GoalStatus,
  { variant: 'success' | 'warning' | 'destructive' | 'outline'; label: string }
> = {
  OnTrack: { variant: 'success', label: 'On track' },
  AtRisk: { variant: 'warning', label: 'At risk' },
  Achieved: { variant: 'outline', label: 'Achieved' },
  Behind: { variant: 'destructive', label: 'Behind' },
};

export function SavingsGoals() {
  const { data, isLoading, isError } = useGoals();

  // Sort by progress descending: achieved / near-target goals surface
  // first so the dashboard celebrates progress instead of just listing
  // raw remaining amounts.
  const top = data
    ? [...data].sort((a, b) => b.progressPercent - a.progressPercent).slice(0, TOP_N)
    : [];

  return (
    <Card data-testid="savings-goals-card" className="h-full">
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center justify-between text-sm font-medium text-muted-foreground">
          <span>Savings goals</span>
          <Target className="h-4 w-4" aria-hidden />
        </CardTitle>
      </CardHeader>
      <CardContent>
        {isError ? (
          <p className="text-sm text-muted-foreground" data-testid="savings-goals-error">
            Failed to load goals.
          </p>
        ) : isLoading || !data ? (
          <SavingsGoalsSkeleton />
        ) : top.length === 0 ? (
          <p className="text-sm text-muted-foreground" data-testid="savings-goals-empty">
            No goals yet.{' '}
            <Link href="/goals" className="underline underline-offset-2">
              Add one in /goals
            </Link>
            .
          </p>
        ) : (
          <ul className="space-y-3" data-testid="savings-goals-list">
            {top.map((g) => (
              <SavingsGoalRow key={g.id} goal={g} />
            ))}
          </ul>
        )}

        {data && data.length > 0 && (
          <p className="mt-4 text-xs">
            <Link
              href="/goals"
              className="text-muted-foreground underline-offset-2 hover:underline"
              data-testid="savings-goals-view-all"
            >
              View all →
            </Link>
          </p>
        )}
      </CardContent>
    </Card>
  );
}

function SavingsGoalRow({ goal }: { goal: GoalDto }) {
  const ratio = goal.progressPercent;
  const cappedRatio = Math.min(Math.max(ratio, 0), PROGRESS_CAP);
  const badge = STATUS_BADGE[goal.status];

  return (
    <li className="space-y-1" data-testid="savings-goals-row" data-status={goal.status}>
      <div className="flex items-center justify-between text-sm">
        <span className="truncate font-medium" title={goal.name}>
          {goal.name}
        </span>
        <Badge variant={badge.variant} data-testid="savings-goals-status-pill">
          {badge.label}
        </Badge>
      </div>
      <div
        className="h-1.5 w-full overflow-hidden rounded-full bg-muted"
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(Math.min(ratio, 1) * 100)}
        aria-label={`${goal.name} progress ${Math.round(ratio * 100)} percent of target`}
      >
        <div
          data-testid="savings-goals-progress-bar"
          data-status={goal.status}
          className={cn('h-full transition-[width]', STATUS_BAR[goal.status])}
          style={{ width: `${cappedRatio * 100}%` }}
        />
      </div>
      <div className="flex items-center justify-between text-xs text-muted-foreground tabular-nums">
        <span>{formatMoney(goal.saved, 'MDL')}</span>
        <span>{formatMoney(goal.targetAmount, 'MDL')}</span>
      </div>
    </li>
  );
}

const SKELETON_ROW_IDS = ['g1', 'g2', 'g3'] as const;

function SavingsGoalsSkeleton() {
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
