'use client';

import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { cn } from '@/src/lib/utils/cn';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';
import type { GoalDetailDto } from '@/src/types/api';

interface CellProps {
  label: string;
  value: string;
  subtitle: string;
  testId: string;
  valueClassName?: string | undefined;
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

interface Props {
  goal: GoalDetailDto;
}

/**
 * Pace card: three small cells side by side (stack on mobile).
 *
 *   1. Average monthly contribution (90-day window) — null when there
 *      isn't enough history yet.
 *   2. Projected completion date at the current pace — null when the
 *      average is null/≤ 0 OR the goal is already achieved.
 *   3. Required monthly contribution to hit the user's target date —
 *      null when no target date OR already achieved.
 *
 * The card consumes the precomputed `pace` payload from the detail DTO
 * directly; we don't recompute anything client-side. Each null cell
 * surfaces a distinct copy so the user can tell *why* the number isn't
 * available.
 */
export function GoalPaceCard({ goal }: Props) {
  const { pace } = goal;
  const isAchieved = goal.status === 'Achieved';

  // --- Avg monthly contribution
  const avgValue =
    pace.avgMonthlyContribution !== null ? formatMoney(pace.avgMonthlyContribution, 'MDL') : '—';
  const avgSubtitle =
    pace.avgMonthlyContribution === null ? 'Not enough history' : 'Based on the last 90 days.';
  const avgClass = pace.avgMonthlyContribution === null ? 'text-muted-foreground' : undefined;

  // --- Projected completion
  const projectedValue = pace.projectedCompletionDate
    ? formatShortDate(pace.projectedCompletionDate)
    : '—';
  let projectedSubtitle: string;
  if (pace.projectedCompletionDate === null) {
    projectedSubtitle = isAchieved ? 'Already achieved' : 'Pace too slow';
  } else if (pace.monthsToAchieveAtPace !== null) {
    projectedSubtitle = `${pace.monthsToAchieveAtPace} months at current pace`;
  } else {
    projectedSubtitle = 'At current pace';
  }
  const projectedClass =
    pace.projectedCompletionDate === null ? 'text-muted-foreground' : undefined;

  // --- Required monthly contribution
  // requiredMonthlyContribution is null when target date is missing OR
  // the goal is achieved. We disambiguate via status + targetDate so the
  // subtitle tells the right story.
  const requiredValue =
    goal.requiredMonthlyContribution !== null
      ? `${formatMoney(goal.requiredMonthlyContribution, 'MDL')} / mo`
      : '—';
  let requiredSubtitle: string;
  if (goal.requiredMonthlyContribution !== null && goal.targetDate) {
    requiredSubtitle = `To reach target by ${formatShortDate(goal.targetDate)}.`;
  } else if (isAchieved) {
    requiredSubtitle = 'Goal already met';
  } else if (!goal.targetDate) {
    requiredSubtitle = 'No target date';
  } else {
    requiredSubtitle = 'No required pace';
  }
  const requiredClass =
    goal.requiredMonthlyContribution === null ? 'text-muted-foreground' : undefined;

  return (
    <Card data-testid="goal-pace-card">
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-medium text-muted-foreground">Pace</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
          <Cell
            label="Avg monthly contribution"
            value={avgValue}
            subtitle={avgSubtitle}
            testId="goal-pace-avg"
            valueClassName={avgClass}
          />
          <Cell
            label="Projected completion"
            value={projectedValue}
            subtitle={projectedSubtitle}
            testId="goal-pace-projected"
            valueClassName={projectedClass}
          />
          <Cell
            label="Required to hit target date"
            value={requiredValue}
            subtitle={requiredSubtitle}
            testId="goal-pace-required"
            valueClassName={requiredClass}
          />
        </div>
        <p className="text-xs text-muted-foreground" data-testid="goal-pace-created">
          Created {formatShortDate(goal.createdOn)}
        </p>
      </CardContent>
    </Card>
  );
}
