'use client';

import { ApiError } from '@/src/lib/api/client';
import { useGoalDetail } from '@/src/lib/api/goals';
import { GoalContributionsTable } from './goal-contributions-table';
import { GoalDetailError } from './goal-detail-error';
import { GoalDetailHeader } from './goal-detail-header';
import { GoalDetailSkeleton } from './goal-detail-skeleton';
import { GoalHistoryChart } from './goal-history-chart';
import { GoalPaceCard } from './goal-pace-card';
import { GoalProgressCard } from './goal-progress-card';

interface Props {
  id: string;
}

/**
 * Top-level Client Component for the goal detail page. Owns the
 * `useGoalDetail` fetch and dispatches to the loading / 404 / error /
 * happy-path layouts.
 *
 * 404 is detected via the typed `ApiError` thrown by the API client —
 * lets us render a distinct copy ("Goal not found.") instead of the
 * generic "Failed to load goal." fallback. Any other failure falls
 * through to the generic state.
 */
export function GoalDetailView({ id }: Props) {
  const { data, isLoading, isError, error } = useGoalDetail(id);

  if (isLoading) {
    return <GoalDetailSkeleton />;
  }

  if (isError) {
    const notFound = error instanceof ApiError && error.status === 404;
    return <GoalDetailError notFound={notFound} />;
  }

  if (!data) {
    // Defensive: !isLoading && !isError but no data → treat as a generic
    // fetch error so the page never silently renders nothing.
    return <GoalDetailError />;
  }

  return (
    <div className="space-y-6" data-testid="goal-detail-view">
      <GoalDetailHeader goal={data} />
      <GoalProgressCard goal={data} />
      <GoalPaceCard goal={data} />
      <GoalHistoryChart goal={data} />
      <GoalContributionsTable goal={data} />
    </div>
  );
}
