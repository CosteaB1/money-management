'use client';

import {
  CartesianGrid,
  Line,
  LineChart,
  ReferenceDot,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  type TooltipProps,
  XAxis,
  YAxis,
} from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';
import type { GoalDetailDto, GoalSavedPointDto } from '@/src/types/api';

const compactMdlFormatter = new Intl.NumberFormat('en-MD', {
  notation: 'compact',
  maximumFractionDigits: 1,
});

// Exported for unit testing — Recharts tooltips don't render through jsdom's
// layout-less DOM, so the content component is verified directly.
export function GoalHistoryTooltip({ active, payload }: TooltipProps<number, string>) {
  if (!active || !payload || payload.length === 0) return null;
  const point = payload[0]?.payload as GoalSavedPointDto | undefined;
  if (!point) return null;
  return (
    <div
      className="rounded-md border bg-popover px-3 py-2 text-xs text-popover-foreground shadow-sm"
      role="tooltip"
    >
      <p className="font-medium">{formatShortDate(point.asOf)}</p>
      <p className="tabular-nums">{formatMoney(point.saved, 'MDL')}</p>
    </div>
  );
}

interface Props {
  goal: GoalDetailDto;
}

/**
 * Saved-over-time line chart. `savedHistory` arrives ascending by `asOf`
 * with monthly cadence — we paint it as-is, layer a dashed reference line
 * at `targetAmount`, and plant a reference dot at `(targetDate,
 * targetAmount)` when both are inside the visible x-range.
 *
 * The sr-only `<ul>` mirrors every point + the target-line/target-dot
 * sentinels so jsdom-bound component tests (where Recharts can't measure
 * its container) can still assert what the chart paints.
 */
export function GoalHistoryChart({ goal }: Props) {
  const points = goal.savedHistory;
  const hasPoints = points.length > 0;

  // Reference dot is only visible when the chart's x-range covers the
  // target date. The history series is monthly — first/last asOf bound
  // the visible x.
  const firstAsOf = hasPoints ? points[0]?.asOf : null;
  const lastAsOf = hasPoints ? points[points.length - 1]?.asOf : null;
  const targetDateInRange =
    goal.targetDate !== null &&
    firstAsOf !== null &&
    lastAsOf !== null &&
    firstAsOf !== undefined &&
    lastAsOf !== undefined &&
    goal.targetDate >= firstAsOf &&
    goal.targetDate <= lastAsOf;

  return (
    <Card data-testid="goal-history-card">
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-medium text-muted-foreground">Saved over time</CardTitle>
      </CardHeader>
      <CardContent>
        {!hasPoints ? (
          <p className="text-sm text-muted-foreground" data-testid="goal-history-empty">
            No history yet
          </p>
        ) : (
          <div
            className="h-72 w-full"
            role="img"
            aria-label={`Saved-over-time history for ${goal.name}`}
            data-testid="goal-history-chart"
          >
            {/* SR-only fallback enumerates everything the chart paints —
                the data points, the target-line reference at `targetAmount`,
                and (when in range) the target-date reference dot. */}
            <ul className="sr-only" data-testid="goal-history-points">
              {points.map((p) => (
                <li key={p.asOf} data-testid="goal-history-point">
                  {formatShortDate(p.asOf)}: {formatMoney(p.saved, 'MDL')}
                </li>
              ))}
              <li data-testid="goal-history-target-line">
                Target: {formatMoney(goal.targetAmount, 'MDL')}
              </li>
              {targetDateInRange && goal.targetDate && (
                <li data-testid="goal-history-target-dot">
                  Target date: {formatShortDate(goal.targetDate)} at{' '}
                  {formatMoney(goal.targetAmount, 'MDL')}
                </li>
              )}
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
                  tickFormatter={(v: number) => `${compactMdlFormatter.format(v)} MDL`}
                  stroke="var(--color-muted-foreground)"
                  fontSize={12}
                  tickLine={false}
                  axisLine={false}
                  width={90}
                />
                <Tooltip
                  content={<GoalHistoryTooltip />}
                  cursor={{ stroke: 'var(--color-border)', strokeWidth: 1 }}
                />
                <ReferenceLine
                  y={goal.targetAmount}
                  stroke="var(--color-muted-foreground)"
                  strokeDasharray="4 4"
                  label={{
                    value: 'Target',
                    position: 'insideTopRight',
                    fontSize: 11,
                    fill: 'var(--color-muted-foreground)',
                  }}
                />
                {targetDateInRange && goal.targetDate && (
                  <ReferenceDot
                    x={goal.targetDate}
                    y={goal.targetAmount}
                    r={5}
                    fill="var(--color-chart-2)"
                    stroke="var(--color-background)"
                    strokeWidth={1.5}
                  />
                )}
                <Line
                  type="monotone"
                  dataKey="saved"
                  name="Saved (MDL)"
                  stroke="var(--color-chart-1)"
                  strokeWidth={2}
                  dot={{ r: 2, fill: 'var(--color-chart-1)' }}
                  activeDot={{ r: 4 }}
                  isAnimationActive={false}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
