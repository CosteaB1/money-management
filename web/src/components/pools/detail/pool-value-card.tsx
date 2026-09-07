'use client';

import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatFractionPercent } from '@/src/lib/utils/pool';
import type { PoolDetailDto } from '@/src/types/api';

interface Props {
  pool: PoolDetailDto;
}

/**
 * The headline card: what the pool is worth, how it splits, and what a share
 * costs.
 *
 * The one subtlety it has to explain is the gap between the **account
 * balance** and the **pool value**. Between closing a month and actually
 * sending the transfer, the account still holds cash that has already been
 * promised to somebody. Counting it as pool value would price the next round
 * of shares against money that is spoken for — and next month the app would
 * read it as fresh profit and pay it out a second time. So the pool's value is
 * deliberately `accountBalance − owed`, and when those two differ the card
 * says so in as many words rather than leaving the user to reconcile two
 * numbers that look like they should match.
 *
 * "Your share" is derived by subtraction (`poolValue − outsideCapital`) so the
 * two halves provably add up to the whole. Each stake is priced independently
 * as `shares × price`, so multiplying instead can leave a one-cent residual
 * hanging between two figures printed side by side.
 */
export function PoolValueCard({ pool }: Props) {
  const yourShare = pool.poolValue - pool.outsideCapital;
  const hasOwedCash = pool.unpaidDistributionCount > 0;

  return (
    <Card data-testid="pool-value-card">
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-medium text-muted-foreground">Pool value</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="space-y-1">
          <p className="text-3xl font-semibold tabular-nums" data-testid="pool-detail-value">
            {formatMoney(pool.poolValue, pool.currency)}
          </p>
          <p
            className="text-sm text-muted-foreground tabular-nums"
            data-testid="pool-detail-value-mdl"
          >
            {pool.poolValueMdl !== null && !pool.missingFxRate ? (
              <>≈ {formatMoney(pool.poolValueMdl, 'MDL')}</>
            ) : (
              // Never a zero: an unconvertible pool shows an em dash under the
              // amber treatment, so the missing rate is visible rather than
              // dressed up as a real figure.
              <span
                className="text-amber-600 dark:text-amber-400"
                title={`No FX rate available for ${pool.currency}. Add one in Settings → FX rates.`}
                data-testid="pool-detail-missing-fx"
              >
                ≈ — (no FX rate for {pool.currency})
              </span>
            )}
          </p>
        </div>

        <div className="grid gap-4 border-t pt-3 sm:grid-cols-3">
          <div className="space-y-1" data-testid="pool-detail-your-share">
            <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
              Your share
            </p>
            <p
              className="text-xl font-semibold tabular-nums"
              data-testid="pool-detail-your-share-amount"
            >
              {formatMoney(yourShare, pool.currency)}
            </p>
            <p
              className="text-xs text-muted-foreground"
              data-testid="pool-detail-your-share-percent"
            >
              {formatFractionPercent(pool.ownerFraction)} of the pool
            </p>
          </div>
          <div className="space-y-1" data-testid="pool-detail-outside-capital">
            <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
              Everyone else
            </p>
            <p
              className="text-xl font-semibold tabular-nums"
              data-testid="pool-detail-outside-capital-amount"
            >
              {formatMoney(pool.outsideCapital, pool.currency)}
            </p>
            <p className="text-xs text-muted-foreground">
              In your account, but not yours. Left out of your net worth.
            </p>
          </div>
          <div className="space-y-1" data-testid="pool-detail-nav">
            <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
              Value per share
            </p>
            <p className="text-xl font-semibold tabular-nums" data-testid="pool-detail-nav-amount">
              {pool.navPerUnit !== null ? (
                formatMoney(pool.navPerUnit, pool.currency)
              ) : (
                <span title="No shares outstanding, so the pool has no price per share.">—</span>
              )}
            </p>
            <p className="text-xs text-muted-foreground">
              Money in and out is priced at this, so nobody else&rsquo;s share moves.
            </p>
          </div>
        </div>

        {hasOwedCash && (
          <div
            className="space-y-1 rounded-md border bg-muted/30 p-3 text-sm"
            data-testid="pool-detail-owed-cash"
          >
            <p className="flex flex-wrap items-baseline justify-between gap-2">
              <span className="text-muted-foreground">{pool.accountName} holds</span>
              <span className="font-medium tabular-nums" data-testid="pool-detail-account-balance">
                {formatMoney(pool.accountBalance, pool.currency)}
              </span>
            </p>
            <p className="flex flex-wrap items-baseline justify-between gap-2">
              <span className="text-muted-foreground">
                Already promised and not yet sent ({pool.unpaidDistributionCount})
              </span>
              <span className="font-medium tabular-nums" data-testid="pool-detail-unpaid-cash">
                −{formatMoney(pool.unpaidDistributionCash, pool.currency)}
              </span>
            </p>
            <p className="pt-1 text-xs text-muted-foreground">
              The account still holds cash from a closed month that has not been sent yet, so the
              pool is worth less than the account shows. Leaving it in would price the next movement
              against money that is already owed — and next month the app would read it as fresh
              profit and pay it out twice.
            </p>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
