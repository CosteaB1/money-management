'use client';

import { Landmark } from 'lucide-react';
import Link from 'next/link';
import { countLabel, MissingFxNote, StatCard } from '@/src/components/dashboard/stat-card';
import { useNetWorth } from '@/src/lib/api/dashboard';
import { formatMoney } from '@/src/lib/utils/currency';

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
 * the trio can never disagree with each other. No extra fetch was added
 * for the pooled-capital line below: `outsideCapitalMdl` arrives on that
 * same payload.
 */
export function TotalAssetsCard() {
  const { data, isLoading, isError } = useNetWorth();

  const missingCount = data?.accountsMissingFxRate ?? 0;
  // How much of the accounts is somebody else's. Zero for every user who
  // has never pooled capital, and the line stays absent in that case —
  // a permanent "0.00 MDL isn't yours" would be noise.
  //
  // It is deliberately NOT subtracted here: `grossAssetsMdl` already
  // excludes it. Other people's money is never counted as the user's
  // asset in the first place, so there is nothing to net out — the line
  // exists to explain why this tile reads lower than the sum of the
  // balances on /accounts, which show pooled accounts in full.
  const outsideCapital = data?.outsideCapitalMdl ?? 0;

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
      {outsideCapital > 0 && (
        <p
          className="mt-2 text-xs text-muted-foreground"
          data-testid="total-assets-outside-capital"
        >
          <Link href="/pools" className="underline underline-offset-2">
            A further {formatMoney(outsideCapital, 'MDL')} sits in your accounts but isn&rsquo;t
            yours — pooled capital, already left out of this total.
          </Link>
        </p>
      )}
      {missingCount > 0 && (
        <MissingFxNote testId="total-assets-missing-rates">
          {countLabel(missingCount, 'account')} missing FX rate — total is incomplete
        </MissingFxNote>
      )}
    </StatCard>
  );
}
