'use client';

import { ApiError } from '@/src/lib/api/client';
import { useLoanDetail } from '@/src/lib/api/loans';
import { formatShortDate } from '@/src/lib/utils/date';
import { LoanDetailError } from './loan-detail-error';
import { LoanDetailHeader } from './loan-detail-header';
import { LoanDetailSkeleton } from './loan-detail-skeleton';
import { LoanPaymentsTable } from './loan-payments-table';
import { LoanProgressCard } from './loan-progress-card';

interface Props {
  id: string;
}

/**
 * Top-level Client Component for the loan detail page. Owns the
 * `useLoanDetail` fetch and dispatches to the loading / 404 / error /
 * happy-path layouts.
 *
 * 404 is detected via the typed `ApiError` thrown by the API client —
 * lets us render a distinct copy ("Loan not found.") instead of the
 * generic "Failed to load loan." fallback. Any other failure falls
 * through to the generic state. Mirrors the goal-detail-view pattern.
 */
export function LoanDetailView({ id }: Props) {
  const { data, isLoading, isError, error } = useLoanDetail(id);

  if (isLoading) {
    return <LoanDetailSkeleton />;
  }

  if (isError) {
    const notFound = error instanceof ApiError && error.status === 404;
    return <LoanDetailError notFound={notFound} />;
  }

  if (!data) {
    // Defensive: !isLoading && !isError but no data → treat as a generic
    // fetch error so the page never silently renders nothing.
    return <LoanDetailError />;
  }

  return (
    <div className="space-y-6" data-testid="loan-detail-view">
      <LoanDetailHeader loan={data} />
      <LoanProgressCard loan={data} />
      {data.disbursementAccountName !== null && (
        <p className="text-sm text-muted-foreground" data-testid="loan-detail-disbursement">
          Disbursed via{' '}
          <span className="font-medium text-foreground">{data.disbursementAccountName}</span> on{' '}
          {formatShortDate(data.loanDate)}.
        </p>
      )}
      <LoanPaymentsTable loan={data} />
    </div>
  );
}
