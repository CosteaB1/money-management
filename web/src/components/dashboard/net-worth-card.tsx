'use client';

import { TrendingUp } from 'lucide-react';
import { countLabel, MissingFxNote, StatCard } from '@/src/components/dashboard/stat-card';
import { useNetWorth } from '@/src/lib/api/dashboard';
import { formatMoney } from '@/src/lib/utils/currency';

/**
 * The third of the three headline tiles: what the user is actually worth.
 *
 * This card used to sum `balanceMdl` across accounts in the browser,
 * which counted borrowed money as if it were the user's own — with 134k
 * MDL of outstanding personal loans it read 183k when the truth was 49k.
 * The figure now comes from `GET /dashboard/net-worth`, which nets the
 * loans out server-side:
 *
 *   netWorth = grossAssets − externalLiabilities + externalAssets
 *
 * Only loans whose disbursement was recorded against an account take
 * part: an unlinked loan's cash never entered a tracked balance, so
 * subtracting the obligation would push the total wrong the other way.
 *
 * Money lent out is only surfaced when it's non-zero: it's the rarer leg
 * (the user usually has none), and a permanent "0.00 MDL owed to you"
 * line is noise. It is always part of the arithmetic regardless — the
 * server does the maths, the UI only decides what to show.
 */
export function NetWorthCard() {
  const { data, isLoading, isError } = useNetWorth();

  // Two independent sources can be short an FX rate, and they understate
  // opposite sides of the equation (a missing account rate makes net
  // worth too low, a missing loan rate makes it too high). Name both so
  // the user knows which way the number is wrong, not just that it is.
  const missingParts: string[] = [];
  if (data && data.accountsMissingFxRate > 0) {
    missingParts.push(countLabel(data.accountsMissingFxRate, 'account'));
  }
  if (data && data.loansMissingFxRate > 0) {
    missingParts.push(countLabel(data.loansMissingFxRate, 'loan'));
  }

  return (
    <StatCard
      title="Net worth"
      icon={TrendingUp}
      amountMdl={data?.netWorthMdl ?? 0}
      caption="Everything you own minus everything you owe, in MDL."
      isLoading={isLoading || !data}
      isError={isError}
      testId="net-worth"
    >
      {data && data.externalAssetsMdl > 0 && (
        <p className="mt-2 text-xs text-muted-foreground" data-testid="net-worth-external-assets">
          Includes {formatMoney(data.externalAssetsMdl, 'MDL')} lent out and still owed to you.
        </p>
      )}
      {missingParts.length > 0 && (
        <MissingFxNote testId="net-worth-missing-rates">
          {missingParts.join(' and ')} missing FX rate — net worth is incomplete
        </MissingFxNote>
      )}
    </StatCard>
  );
}
