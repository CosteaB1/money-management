'use client';

import { TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/src/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/src/components/ui/dialog';
import { useArchiveLoan } from '@/src/lib/api/loans';
import { formatMoney } from '@/src/lib/utils/currency';
import type { LoanDto } from '@/src/types/api';

interface Props {
  loan: LoanDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Confirms archiving (soft-delete) of a loan. Archived loans drop off the
 * list, but their payment history and any linked transactions stay intact
 * and the loan remains viewable on its detail page.
 *
 * When the loan still has an outstanding balance, an amber warning line is
 * appended to the confirm copy — archiving hides the loan from the list
 * and summary tiles without settling it, which is easy to mistake for
 * "marking it paid".
 */
export function ArchiveLoanDialog({ loan, open, onOpenChange }: Props) {
  const archive = useArchiveLoan();
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleConfirm = async () => {
    setIsSubmitting(true);
    try {
      await archive.mutateAsync(loan.id);
      toast.success(`Archived "${loan.counterparty}" loan`);
      onOpenChange(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to archive loan');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent data-testid="archive-loan-dialog">
        <DialogHeader>
          <DialogTitle>Archive loan?</DialogTitle>
          <DialogDescription>
            Archiving hides the loan with <strong>{loan.counterparty}</strong> from the list. Its
            payment history and any linked transactions stay intact, and the loan remains viewable
            on its detail page.
            {/* Outstanding-balance warning: archiving is NOT settling, so
                flag when money is still owed. Lives inside the
                DialogDescription so aria-describedby announces it with the
                dialog for screen readers; span-based (Radix's Description
                renders a <p>, which can't contain block elements). Amber +
                TriangleAlert matches the import-preview reconciliation
                banner's warning treatment. */}
            {loan.outstanding > 0 && (
              <span
                data-testid="archive-loan-outstanding-warning"
                className="mt-3 flex items-start gap-2 text-amber-700 dark:text-amber-400"
              >
                <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
                <span>
                  This loan still has <strong>{formatMoney(loan.outstanding, loan.currency)}</strong>{' '}
                  outstanding. Archiving hides it from your list and totals without settling it —
                  the money movements on any linked account remain.
                </span>
              </span>
            )}
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button
            type="button"
            variant="destructive"
            disabled={isSubmitting}
            onClick={handleConfirm}
            data-testid="archive-loan-confirm-button"
          >
            {isSubmitting ? 'Archiving...' : 'Archive'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
