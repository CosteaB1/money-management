'use client';

import { Badge } from '@/src/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { cn } from '@/src/lib/utils/cn';
import { formatMoney } from '@/src/lib/utils/currency';
import type { GoalDetailDto, GoalStatus } from '@/src/types/api';

// Visual cap so a 300%-overshot goal doesn't blow out the layout —
// mirrors the goals-table progress bar.
const PROGRESS_CAP = 1.2;

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

const percentFormatter = new Intl.NumberFormat('en-US', {
  style: 'percent',
  maximumFractionDigits: 0,
});

interface Props {
  goal: GoalDetailDto;
}

/**
 * Progress card: oversized `Saved` figure with the percentage of target,
 * a horizontal progress bar capped at 120% (same cap as the list-page row),
 * remaining-to-go, and a color-coded status pill mirroring the
 * goals-table conventions. Surfaces the amber `missingFxRate` warning
 * when the linked account couldn't be FX-converted.
 */
export function GoalProgressCard({ goal }: Props) {
  const ratio = goal.progressPercent;
  const cappedRatio = Math.min(Math.max(ratio, 0), PROGRESS_CAP);
  const widthPct = `${cappedRatio * 100}%`;
  const badge = STATUS_BADGE[goal.status];
  // `progressPercent` is the raw saved/target ratio; show the user the
  // capped-at-100% percentage so an overshot goal doesn't read as e.g.
  // "326% of target" in the subtitle — that's already clear from the
  // Saved vs Target numbers above.
  const percentLabel = percentFormatter.format(Math.min(Math.max(ratio, 0), 1));

  return (
    <Card data-testid="goal-progress-card" data-status={goal.status}>
      <CardHeader className="flex flex-row items-center justify-between gap-3 pb-3">
        <CardTitle className="text-sm font-medium text-muted-foreground">Progress</CardTitle>
        <Badge
          variant={badge.variant}
          data-testid="goal-detail-status-pill"
          aria-label={`Status: ${badge.label}`}
        >
          {badge.label}
        </Badge>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="space-y-1">
          <p className="text-3xl font-semibold tabular-nums" data-testid="goal-detail-saved">
            {formatMoney(goal.saved, 'MDL')}
          </p>
          <p className="text-sm text-muted-foreground" data-testid="goal-detail-progress-subtitle">
            {percentLabel} of {formatMoney(goal.targetAmount, 'MDL')}
          </p>
        </div>

        <div
          className="h-3 w-full overflow-hidden rounded-full bg-muted"
          role="progressbar"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(Math.min(ratio, 1) * 100)}
          aria-label={`${goal.name} progress ${Math.round(ratio * 100)} percent of target`}
        >
          <div
            data-testid="goal-detail-progress-bar"
            data-status={goal.status}
            className={cn(
              'h-full transition-[width]',
              STATUS_BAR[goal.status],
              goal.status === 'Achieved' && 'opacity-80 ring-1 ring-inset ring-emerald-400/60',
            )}
            style={{ width: widthPct }}
          />
        </div>

        <p className="text-sm text-muted-foreground" data-testid="goal-detail-remaining">
          Remaining:{' '}
          <span className="tabular-nums">
            {goal.remaining > 0 ? formatMoney(goal.remaining, 'MDL') : '—'}
          </span>
        </p>

        {goal.missingFxRate && (
          <output
            className="block text-xs text-amber-600 dark:text-amber-400"
            data-testid="goal-detail-missing-fx"
          >
            Some linked-account rows lack FX rates — saved/contributions may be incomplete.
          </output>
        )}
      </CardContent>
    </Card>
  );
}
