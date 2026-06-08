'use client';

import { TrendingUp } from 'lucide-react';
import Link from 'next/link';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { useAccounts } from '@/src/lib/api/accounts';
import { formatMoney } from '@/src/lib/utils/currency';

export function NetWorthCard() {
  const { data, isLoading, isError } = useAccounts(false);

  const netWorth = data?.reduce((sum, account) => sum + (account.balanceMdl ?? 0), 0) ?? 0;
  const missingCount = data?.filter((a) => a.balanceMdl === null).length ?? 0;

  return (
    <Card data-testid="net-worth-card" className="h-full">
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center justify-between text-sm font-medium text-muted-foreground">
          <span>Net worth</span>
          <TrendingUp className="h-4 w-4" aria-hidden />
        </CardTitle>
      </CardHeader>
      <CardContent>
        {isError ? (
          <p className="text-sm text-destructive">Failed to load.</p>
        ) : isLoading || !data ? (
          <div
            className="h-10 w-32 animate-pulse rounded bg-muted"
            role="status"
            aria-label="Loading"
          />
        ) : (
          <p
            className="text-3xl font-semibold tracking-tight tabular-nums"
            data-testid="net-worth-amount"
          >
            {formatMoney(netWorth, 'MDL')}
          </p>
        )}
        <p className="mt-2 text-xs text-muted-foreground">
          Sum of all non-archived account balances in MDL.
        </p>
        {missingCount > 0 && (
          <p
            className="mt-2 text-xs text-amber-600 dark:text-amber-400"
            data-testid="net-worth-missing-rates"
          >
            <Link href="/settings/fx-rates" className="underline underline-offset-2">
              {missingCount} account{missingCount === 1 ? '' : 's'} missing FX rate
            </Link>
          </p>
        )}
      </CardContent>
    </Card>
  );
}
