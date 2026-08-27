'use client';

import { Badge } from '@/src/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { cn } from '@/src/lib/utils/cn';
import { formatMoney } from '@/src/lib/utils/currency';
import type { LoanDetailDto, LoanStatus } from '@/src/types/api';

// Repayment progress caps at 100% — payments can never exceed the
// principal. Mirrors the loans-table progress bar.
const PROGRESS_CAP = 1;

const STATUS_BADGE: Record<LoanStatus, { variant: 'success' | 'outline'; label: string }> = {
  Active: { variant: 'success', label: 'Active' },
  Settled: { variant: 'outline', label: 'Settled' },
};

interface Props {
  loan: LoanDetailDto;
}

/**
 * Progress card: oversized outstanding balance in the loan's native
 * currency (with a muted MDL-equivalent line for non-MDL loans), a
 * "repaid X of Y" subtitle, a repayment progress bar capped at 100%, and
 * the Active/Settled status pill. Surfaces the amber `missingFxRate`
 * warning via the house `<output>` pattern when the outstanding balance
 * couldn't be converted to MDL.
 */
export function LoanProgressCard({ loan }: Props) {
  const ratio = loan.totalRepaid / loan.principal;
  const cappedRatio = Math.min(Math.max(ratio, 0), PROGRESS_CAP);
  const widthPct = `${cappedRatio * 100}%`;
  const badge = STATUS_BADGE[loan.status];

  return (
    <Card data-testid="loan-progress-card" data-status={loan.status}>
      <CardHeader className="flex flex-row items-center justify-between gap-3 pb-3">
        <CardTitle className="text-sm font-medium text-muted-foreground">Outstanding</CardTitle>
        <Badge
          variant={badge.variant}
          data-testid="loan-detail-status-pill"
          aria-label={`Status: ${badge.label}`}
        >
          {badge.label}
        </Badge>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="space-y-1">
          <p className="text-3xl font-semibold tabular-nums" data-testid="loan-detail-outstanding">
            {formatMoney(loan.outstanding, loan.currency)}
          </p>
          {loan.currency !== 'MDL' && loan.outstandingMdl !== null && (
            <p
              className="text-sm text-muted-foreground tabular-nums"
              data-testid="loan-detail-outstanding-mdl"
            >
              {formatMoney(loan.outstandingMdl, 'MDL')}
            </p>
          )}
          <p className="text-sm text-muted-foreground" data-testid="loan-detail-progress-subtitle">
            Repaid {formatMoney(loan.totalRepaid, loan.currency)} of{' '}
            {formatMoney(loan.principal, loan.currency)}
          </p>
        </div>

        <div
          className="h-3 w-full overflow-hidden rounded-full bg-muted"
          role="progressbar"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(cappedRatio * 100)}
          aria-label={`${loan.counterparty} loan repaid ${Math.round(cappedRatio * 100)} percent`}
        >
          <div
            data-testid="loan-detail-progress-bar"
            data-status={loan.status}
            className={cn(
              'h-full bg-emerald-500 transition-[width]',
              loan.status === 'Settled' && 'opacity-80 ring-1 ring-inset ring-emerald-400/60',
            )}
            style={{ width: widthPct }}
          />
        </div>

        {loan.missingFxRate && (
          <output
            className="block text-xs text-amber-600 dark:text-amber-400"
            data-testid="loan-detail-missing-fx"
          >
            No FX rate available to convert the outstanding balance to MDL — add one in Settings →
            FX rates.
          </output>
        )}
      </CardContent>
    </Card>
  );
}
