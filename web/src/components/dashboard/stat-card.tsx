'use client';

import type { LucideIcon } from 'lucide-react';
import Link from 'next/link';
import type { ReactNode } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { formatMoney } from '@/src/lib/utils/currency';

/**
 * Shared shell for the three headline dashboard tiles — Total assets,
 * I owe, Net worth.
 *
 * All three read the same `GET /dashboard/net-worth` payload and sit
 * side by side in row 1, so any drift between them (a different skeleton
 * height, a differently worded error) reads as a bug. Extracting the
 * shell keeps one loading state, one error copy, and one type ramp
 * instead of three copies that quietly diverge.
 *
 * The visual pattern is lifted from `loans/loans-summary.tsx`'s
 * SummaryTile: muted small title with a trailing icon, 3xl tabular
 * amount, muted caption. `children` renders below the caption and is
 * where each tile hangs its own extras (missing-FX warnings, deep
 * links) — those carry per-card `data-testid`s that must stay owned by
 * the concrete card rather than be synthesised from a prefix here.
 */
export function StatCard({
  title,
  icon: Icon,
  amountMdl,
  caption,
  isLoading,
  isError,
  testId,
  children,
}: {
  title: string;
  icon: LucideIcon;
  amountMdl: number;
  caption: string;
  isLoading: boolean;
  isError: boolean;
  /** Prefix for the card's own testids: `${testId}-card` / `${testId}-amount`. */
  testId: string;
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
          <p
            className="text-3xl font-semibold tracking-tight tabular-nums"
            data-testid={`${testId}-amount`}
          >
            {formatMoney(amountMdl, 'MDL')}
          </p>
        )}
        <p className="mt-2 text-xs text-muted-foreground">{caption}</p>
        {children}
      </CardContent>
    </Card>
  );
}

/**
 * Amber "this total is incomplete" line, shared by the three tiles.
 *
 * A missing FX rate makes the offending balance contribute 0 rather than
 * blow up the request, so the number on screen is *understated* and
 * looks perfectly plausible. The warning is the only thing standing
 * between the user and a wrong figure they'd trust — hence the deep link
 * straight to the page where they can fix it.
 */
export function MissingFxNote({ testId, children }: { testId: string; children: ReactNode }) {
  return (
    <p className="mt-2 text-xs text-amber-600 dark:text-amber-400" data-testid={testId}>
      <Link href="/settings/fx-rates" className="underline underline-offset-2">
        {children}
      </Link>
    </p>
  );
}

/** `2, 'account'` → `"2 accounts"`. Naive -s plural; all callers are regular nouns. */
export function countLabel(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}
