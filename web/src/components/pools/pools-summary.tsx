'use client';

import { Clock, type LucideIcon, PieChart, Users, Wallet } from 'lucide-react';
import Link from 'next/link';
import type { ReactNode } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { usePools } from '@/src/lib/api/pools';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatSharePercent } from '@/src/lib/utils/pool';
import type { PoolDto } from '@/src/types/api';

/**
 * Summary tiles above the pools table.
 *
 * Four figures, in the order the user needs them:
 *  1. **Your share** — how much of the pooled money is actually theirs.
 *  2. **Outside capital** — what they hold on the participants' behalf.
 *  3. **Pool value** — the whole thing, which is what the accounts page shows.
 *  4. **Unpaid payouts** — only when a close has happened but the cash has not
 *     left yet. Absent the rest of the time, because a permanent zero here
 *     would train the user to ignore the one number that matters most.
 *
 * Two aggregation decisions worth knowing:
 *
 * - **Your share is derived as `poolValue − outsideCapital`, not as
 *   `poolValue × ownerFraction`.** Both are right; the subtraction is chosen so
 *   the first two tiles provably add up to the third. Each stake is priced
 *   independently as `units × nav`, so the multiplication can leave a
 *   one-cent residual unassigned — visible, and confusing, when it lands
 *   between two tiles sitting side by side.
 *
 * - **The share percentage is weighted by MDL value across pools**, which
 *   equals the pool's own `ownerFraction` in the one-pool case that actually
 *   exists. With nothing convertible to MDL there is no honest weighting, so
 *   the percentage reads `—` rather than a fabricated 0%.
 *
 * Missing-FX treatment mirrors `LoansSummary`: an unconvertible pool
 * contributes 0 and an amber line says the total is incomplete, instead of a
 * wrong number rendered with total confidence.
 */
export function PoolsSummary() {
  const { data, isLoading, isError } = usePools();

  // The summary's own query is active-only, but filter defensively — the same
  // guard LoansSummary keeps, in case this is ever fed the "Show archived"
  // list. An archived pool holds no outside units by definition, so counting
  // one would only inflate the user's own side.
  const active = data?.filter((p) => !p.isArchived) ?? [];
  const convertible = active.filter(
    (p) => !p.missingFxRate && p.poolValueMdl !== null && p.outsideCapitalMdl !== null,
  );

  const poolValueMdl = convertible.reduce((sum, p) => sum + (p.poolValueMdl ?? 0), 0);
  const outsideCapitalMdl = convertible.reduce((sum, p) => sum + (p.outsideCapitalMdl ?? 0), 0);
  const yourShareMdl = poolValueMdl - outsideCapitalMdl;
  const sharePercent =
    poolValueMdl > 0 ? formatSharePercent((yourShareMdl / poolValueMdl) * 100) : '—';

  const missingCount = active.length - convertible.length;

  const unpaid = active.filter((p) => p.unpaidDistributionCount > 0);
  const unpaidCount = unpaid.reduce((sum, p) => sum + p.unpaidDistributionCount, 0);
  const busy = isLoading || !data;

  return (
    <div
      className={
        unpaid.length > 0
          ? 'grid gap-4 sm:grid-cols-2 xl:grid-cols-4'
          : 'grid gap-4 sm:grid-cols-2 xl:grid-cols-3'
      }
      data-testid="pools-summary"
    >
      <PoolTile
        title="Your share"
        icon={PieChart}
        value={formatMoney(yourShareMdl, 'MDL')}
        subtitle="What the pooled money is worth to you, in MDL."
        testId="your-share"
        isLoading={busy}
        isError={isError}
      >
        <p className="mt-1 text-sm font-medium tabular-nums" data-testid="your-share-percent">
          {sharePercent} of the pool
        </p>
      </PoolTile>

      <PoolTile
        title="Outside capital"
        icon={Users}
        value={formatMoney(outsideCapitalMdl, 'MDL')}
        subtitle="Held on the participants' behalf — in your account, but not yours."
        testId="outside-capital"
        isLoading={busy}
        isError={isError}
      />

      <PoolTile
        title="Pool value"
        icon={Wallet}
        value={formatMoney(poolValueMdl, 'MDL')}
        subtitle="Everything in the pool, yours and theirs together."
        testId="pool-value"
        isLoading={busy}
        isError={isError}
      >
        {missingCount > 0 && (
          <p
            className="mt-2 text-xs text-amber-600 dark:text-amber-400"
            data-testid="pools-missing-fx"
          >
            <Link href="/settings/fx-rates" className="underline underline-offset-2">
              {missingCount} pool{missingCount === 1 ? '' : 's'} missing FX rate — totals are
              incomplete
            </Link>
          </p>
        )}
      </PoolTile>

      {unpaid.length > 0 && (
        <PoolTile
          title="Unpaid payouts"
          icon={Clock}
          value={unpaidCashLabel(unpaid)}
          subtitle="Closed and owed, still sitting in the account. Settle each one on the day it actually leaves."
          testId="unpaid-payouts"
          isLoading={busy}
          isError={isError}
        >
          <p className="mt-1 text-sm font-medium" data-testid="unpaid-payouts-count">
            {unpaidCount} payout{unpaidCount === 1 ? '' : 's'} owed
          </p>
        </PoolTile>
      )}
    </div>
  );
}

/**
 * The owed cash, in the pool currency when every pool with an unpaid payout
 * shares one — which is the only case the data model can express honestly.
 *
 * `unpaidDistributionCash` is native-only; the API exposes no MDL equivalent
 * for it, so summing across two currencies would be inventing a number. With
 * more than one currency in play the count line below carries the message
 * instead.
 */
function unpaidCashLabel(unpaid: PoolDto[]): string {
  const currencies = new Set(unpaid.map((p) => p.currency));
  const [only] = [...currencies];
  if (currencies.size !== 1 || only === undefined) return 'Multiple currencies';
  return formatMoney(
    unpaid.reduce((sum, p) => sum + p.unpaidDistributionCash, 0),
    only,
  );
}

function PoolTile({
  title,
  icon: Icon,
  value,
  subtitle,
  testId,
  isLoading,
  isError,
  children,
}: {
  title: string;
  icon: LucideIcon;
  value: string;
  subtitle: string;
  testId: string;
  isLoading: boolean;
  isError: boolean;
  children?: ReactNode;
}) {
  return (
    <Card data-testid={`${testId}-card`} className="h-full">
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center justify-between text-sm font-medium text-muted-foreground">
          <span>{title}</span>
          <Icon className="h-4 w-4" aria-hidden />
        </CardTitle>
      </CardHeader>
      <CardContent>
        {isError ? (
          <p className="text-sm text-destructive">Failed to load.</p>
        ) : isLoading ? (
          <div
            className="h-10 w-32 animate-pulse rounded bg-muted"
            role="status"
            aria-label="Loading"
          />
        ) : (
          <>
            <p
              className="text-3xl font-semibold tracking-tight tabular-nums"
              data-testid={`${testId}-amount`}
            >
              {value}
            </p>
            {children}
          </>
        )}
        <p className="mt-2 text-xs text-muted-foreground">{subtitle}</p>
      </CardContent>
    </Card>
  );
}
