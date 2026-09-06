'use client';

import { Landmark } from 'lucide-react';
import { countLabel, MissingFxNote, StatCard } from '@/src/components/dashboard/stat-card';
import { useNetWorth } from '@/src/lib/api/dashboard';

/**
 * First of the three headline tiles: the gross side of the net-worth
 * equation — what sits in the user's accounts, before any loan is netted
 * out.
 *
 * This is the number the old single "Net worth" card actually showed, so
 * it stays on the dashboard under an honest label rather than
 * disappearing: seeing 183k assets / 134k owed / 49k net is what makes
 * the corrected net worth legible instead of alarming.
 *
 * Reads the same `GET /dashboard/net-worth` payload as its two
 * neighbours — one request feeds all three (TanStack Query dedupes) and
 * the trio can never disagree with each other.
 */
export function TotalAssetsCard() {
  const { data, isLoading, isError } = useNetWorth();

  const missingCount = data?.accountsMissingFxRate ?? 0;

  return (
    <StatCard
      title="Total assets"
      icon={Landmark}
      amountMdl={data?.grossAssetsMdl ?? 0}
      caption="Sum of all non-archived account balances in MDL, before what you owe."
      isLoading={isLoading || !data}
      isError={isError}
      testId="total-assets"
    >
      {missingCount > 0 && (
        <MissingFxNote testId="total-assets-missing-rates">
          {countLabel(missingCount, 'account')} missing FX rate — total is incomplete
        </MissingFxNote>
      )}
    </StatCard>
  );
}
