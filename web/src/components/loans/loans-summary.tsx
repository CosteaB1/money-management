'use client';

import { ArrowDownLeft, ArrowUpRight, type LucideIcon } from 'lucide-react';
import Link from 'next/link';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { useLoans } from '@/src/lib/api/loans';
import { formatMoney } from '@/src/lib/utils/currency';
import type { LoanDto } from '@/src/types/api';

/**
 * Two summary tiles above the loans table: "Owed to me" (Σ outstanding
 * MDL-equivalents of Given loans) and "I owe" (Σ of Received loans).
 *
 * Mirrors the net-worth-card missing-FX treatment: loans whose
 * outstanding couldn't be FX-converted contribute 0 to the sum, and an
 * amber warning line (linking to Settings → FX rates) flags that the
 * shown total is incomplete rather than silently rendering a wrong one.
 */
export function LoansSummary() {
  const { data, isLoading, isError } = useLoans();

  // Archiving hides a loan from the list and totals *without* settling it,
  // so an archived loan's outstanding must never inflate the tiles. The
  // summary's own query is active-only, but filter defensively anyway in
  // case the fetched list ever contains archived rows (e.g. if this
  // component is later fed the "Show archived" list).
  const active = data?.filter((l) => !l.isArchived) ?? [];
  const given = active.filter((l) => l.direction === 'Given');
  const received = active.filter((l) => l.direction === 'Received');

  return (
    <div className="grid gap-4 md:grid-cols-2" data-testid="loans-summary">
      <SummaryTile
        title="Owed to me"
        subtitle="Outstanding on loans you lent, in MDL."
        icon={ArrowDownLeft}
        loans={given}
        isLoading={isLoading || !data}
        isError={isError}
        testId="owed-to-me"
      />
      <SummaryTile
        title="I owe"
        subtitle="Outstanding on loans you borrowed, in MDL."
        icon={ArrowUpRight}
        loans={received}
        isLoading={isLoading || !data}
        isError={isError}
        testId="i-owe"
      />
    </div>
  );
}

function SummaryTile({
  title,
  subtitle,
  icon: Icon,
  loans,
  isLoading,
  isError,
  testId,
}: {
  title: string;
  subtitle: string;
  icon: LucideIcon;
  loans: LoanDto[];
  isLoading: boolean;
  isError: boolean;
  testId: string;
}) {
  const total = loans.reduce((sum, loan) => sum + (loan.outstandingMdl ?? 0), 0);
  const missingCount = loans.filter(
    (loan) => loan.missingFxRate || loan.outstandingMdl === null,
  ).length;

  return (
    <Card data-testid={`${testId}-card`}>
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
          <p
            className="text-3xl font-semibold tracking-tight tabular-nums"
            data-testid={`${testId}-amount`}
          >
            {formatMoney(total, 'MDL')}
          </p>
        )}
        <p className="mt-2 text-xs text-muted-foreground">{subtitle}</p>
        {missingCount > 0 && (
          <p
            className="mt-2 text-xs text-amber-600 dark:text-amber-400"
            data-testid={`${testId}-missing-fx`}
          >
            <Link href="/settings/fx-rates" className="underline underline-offset-2">
              {missingCount} loan{missingCount === 1 ? '' : 's'} missing FX rate — total is
              incomplete
            </Link>
          </p>
        )}
      </CardContent>
    </Card>
  );
}
