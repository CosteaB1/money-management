'use client';

import { ApiError } from '@/src/lib/api/client';
import { usePoolDetail } from '@/src/lib/api/pools';
import { MarkStalenessWarning } from './mark-staleness-warning';
import { PoolDetailError } from './pool-detail-error';
import { PoolDetailHeader } from './pool-detail-header';
import { PoolDetailSkeleton } from './pool-detail-skeleton';
import { PoolEventsTable } from './pool-events-table';
import { PoolParticipantsTable } from './pool-participants-table';
import { PoolReconciliationPanel } from './pool-reconciliation-panel';
import { PoolValueCard } from './pool-value-card';

interface Props {
  id: string;
}

/**
 * Top-level Client Component for the pool detail page. Owns the
 * `usePoolDetail` fetch and dispatches to the loading / 404 / error / happy
 * paths, mirroring `LoanDetailView`.
 *
 * Section order is deliberate. The **staleness warning sits above the value**:
 * a figure the user has not confirmed in three weeks should be doubted before
 * it is read, not after. The **reconciliation panel sits above the roster**
 * for the same reason — when the ledger and the account disagree, every
 * percentage below it is derived from a total that is wrong, and the user
 * needs to know that before they start reading names and amounts.
 */
export function PoolDetailView({ id }: Props) {
  const { data, isLoading, isError, error } = usePoolDetail(id);

  if (isLoading) {
    return <PoolDetailSkeleton />;
  }

  if (isError) {
    const notFound = error instanceof ApiError && error.status === 404;
    return <PoolDetailError notFound={notFound} />;
  }

  if (!data) {
    // Defensive: !isLoading && !isError but no data → treat as a generic
    // fetch error so the page never silently renders nothing.
    return <PoolDetailError />;
  }

  return (
    <div className="space-y-6" data-testid="pool-detail-view">
      <PoolDetailHeader pool={data} />
      <MarkStalenessWarning lastMarkDate={data.lastMarkDate} markAgeDays={data.markAgeDays} />
      <PoolValueCard pool={data} />
      <PoolReconciliationPanel pool={data} />
      <PoolParticipantsTable pool={data} />
      <PoolEventsTable pool={data} />
      {data.notes !== null && (
        <p className="text-sm text-muted-foreground" data-testid="pool-detail-notes">
          {data.notes}
        </p>
      )}
    </div>
  );
}
