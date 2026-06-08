'use client';

import { useAccountDetail } from '@/src/lib/api/accounts';
import { ApiError } from '@/src/lib/api/client';
import { AccountDetailError } from './account-detail-error';
import { AccountDetailHeader } from './account-detail-header';
import { AccountDetailSkeleton } from './account-detail-skeleton';
import { ActivitySection } from './activity-section';
import { BalanceTrendCard } from './balance-trend-card';
import { PerformanceCard } from './performance-card';

interface Props {
  id: string;
}

/**
 * Top-level Client Component for the account detail page. Owns the
 * useAccountDetail fetch and dispatches to the loading / 404 / error /
 * happy-path layouts.
 *
 * 404 is detected via the typed ApiError thrown by the API client —
 * lets us render a distinct copy ("Account not found.") instead of the
 * generic "Failed to load account." fallback. Any other failure falls
 * through to the generic state.
 */
export function AccountDetailView({ id }: Props) {
  const { data, isLoading, isError, error } = useAccountDetail(id);

  if (isLoading) {
    return <AccountDetailSkeleton />;
  }

  if (isError) {
    const notFound = error instanceof ApiError && error.status === 404;
    return <AccountDetailError notFound={notFound} />;
  }

  if (!data) {
    // Defensive: !isLoading && !isError but no data → treat as a generic
    // fetch error so the page never silently renders nothing.
    return <AccountDetailError />;
  }

  return (
    <div className="space-y-6" data-testid="account-detail-view">
      <AccountDetailHeader account={data} />
      <PerformanceCard account={data} />
      <BalanceTrendCard account={data} />
      <ActivitySection
        accountId={data.id}
        openingBalance={data.initialCapital}
        openingDate={data.openingDate}
        currency={data.currency}
      />
    </div>
  );
}
