'use client';

import { ArrowUpRight } from 'lucide-react';
import Link from 'next/link';
import { countLabel, MissingFxNote, StatCard } from '@/src/components/dashboard/stat-card';
import { useNetWorth } from '@/src/lib/api/dashboard';

/**
 * Second of the three headline tiles: outstanding personal debt — the
 * amount subtracted from Total assets to get Net worth.
 *
 * Rendered as a positive magnitude (the DTO reports it that way) because
 * "I owe 134,380 MDL" reads better than a negative number; the sign
 * lives in the net-worth arithmetic, not on screen.
 *
 * Deliberately duplicates the loans page's own "I owe" tile — this is
 * the dashboard's summary of it — so it uses a distinct
 * `i-owe-dashboard-*` testid namespace to avoid colliding with
 * `loans/loans-summary.tsx`'s `i-owe-card` when a test renders both.
 *
 * The two totals can legitimately differ: this one is the net-worth
 * term, so it counts archived loans (a debt hidden from the list is
 * still a debt) and skips loans with no linked disbursement, while the
 * loans page tile mirrors exactly what its table shows.
 */
export function IOweCard() {
  const { data, isLoading, isError } = useNetWorth();

  const missingCount = data?.loansMissingFxRate ?? 0;

  return (
    <StatCard
      title="I owe"
      icon={ArrowUpRight}
      amountMdl={data?.externalLiabilitiesMdl ?? 0}
      caption="Outstanding on loans you borrowed, in MDL."
      isLoading={isLoading || !data}
      isError={isError}
      testId="i-owe-dashboard"
    >
      <p className="mt-2 text-xs">
        <Link
          href="/loans"
          className="text-muted-foreground underline underline-offset-2"
          data-testid="i-owe-dashboard-link"
        >
          View loans
        </Link>
      </p>
      {missingCount > 0 && (
        <MissingFxNote testId="i-owe-dashboard-missing-rates">
          {countLabel(missingCount, 'loan')} missing FX rate — total is incomplete
        </MissingFxNote>
      )}
    </StatCard>
  );
}
