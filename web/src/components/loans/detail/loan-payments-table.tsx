'use client';

import { Trash2 } from 'lucide-react';
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
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/src/components/ui/table';
import { useDeleteLoanPayment } from '@/src/lib/api/loans';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';
import type { LoanDetailDto, LoanPaymentDto } from '@/src/types/api';

// Notes are truncated to keep the table dense — the full text is exposed
// via the native `title` attribute on hover. Mirrors the goal
// contributions table.
const NOTES_TRUNCATE = 60;

function truncate(value: string, max: number): string {
  if (value.length <= max) return value;
  return `${value.slice(0, max).trimEnd()}…`;
}

interface Props {
  loan: LoanDetailDto;
}

/**
 * Payment history table. Rows arrive newest-first from the backend and
 * render as given. Each row exposes a delete control with a confirm
 * dialog; when the payment is linked to a transaction, the confirm copy
 * warns that the transaction is deleted too.
 */
export function LoanPaymentsTable({ loan }: Props) {
  const rows = loan.payments;

  return (
    <div className="space-y-3" data-testid="loan-payments-section">
      <h2 className="text-base font-semibold tracking-tight">Payments</h2>
      <div className="rounded-lg border">
        <Table data-testid="loan-payments-table">
          <TableHeader>
            <TableRow>
              <TableHead>Date</TableHead>
              <TableHead className="text-right">Amount</TableHead>
              <TableHead>Account</TableHead>
              <TableHead>Notes</TableHead>
              <TableHead className="w-12 text-right">
                <span className="sr-only">Actions</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.length === 0 ? (
              <TableRow>
                <TableCell
                  colSpan={5}
                  className="py-10 text-center text-muted-foreground"
                  data-testid="loan-payments-empty"
                >
                  No payments recorded yet.
                </TableCell>
              </TableRow>
            ) : (
              rows.map((payment) => {
                const notes = payment.notes ?? '';
                return (
                  <TableRow key={payment.id} data-testid="loan-payment-row">
                    <TableCell className="text-muted-foreground">
                      {formatShortDate(payment.occurredOn)}
                    </TableCell>
                    <TableCell
                      className="text-right font-medium tabular-nums"
                      data-testid="loan-payment-amount"
                    >
                      {formatMoney(payment.amount, payment.currency)}
                    </TableCell>
                    <TableCell className="text-muted-foreground" data-testid="loan-payment-account">
                      {payment.accountName ?? <span aria-hidden>—</span>}
                    </TableCell>
                    <TableCell className="max-w-[24rem] text-muted-foreground">
                      {notes ? (
                        <span title={notes} data-testid="loan-payment-notes">
                          {truncate(notes, NOTES_TRUNCATE)}
                        </span>
                      ) : (
                        <span aria-hidden>—</span>
                      )}
                    </TableCell>
                    <TableCell className="text-right">
                      <DeletePaymentControl loanId={loan.id} payment={payment} />
                    </TableCell>
                  </TableRow>
                );
              })
            )}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}

/**
 * Ghost icon-button + destructive confirm dialog for a single payment.
 * Kept self-contained (mirrors the transactions-table delete control) so
 * the dialog's open state is scoped per-row.
 */
function DeletePaymentControl({ loanId, payment }: { loanId: string; payment: LoanPaymentDto }) {
  const [open, setOpen] = useState(false);
  const deletePayment = useDeleteLoanPayment(loanId);

  const handleConfirm = async () => {
    try {
      await deletePayment.mutateAsync(payment.id);
      toast.success('Payment deleted');
      setOpen(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to delete payment');
    }
  };

  return (
    <>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="h-8 w-8 text-muted-foreground hover:text-destructive"
        aria-label={`Delete payment of ${formatMoney(payment.amount, payment.currency)}`}
        data-testid="delete-payment"
        onClick={() => setOpen(true)}
      >
        <Trash2 aria-hidden />
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent data-testid="delete-payment-dialog">
          <DialogHeader>
            <DialogTitle>Delete payment?</DialogTitle>
            <DialogDescription>
              This removes the {formatMoney(payment.amount, payment.currency)} payment from{' '}
              {formatShortDate(payment.occurredOn)} and restores the outstanding balance.{' '}
              {payment.transactionId !== null && (
                <span data-testid="delete-payment-linked-warning">
                  This will also delete the linked transaction on{' '}
                  <strong>{payment.accountName ?? 'the linked account'}</strong>.
                </span>
              )}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              disabled={deletePayment.isPending}
              onClick={handleConfirm}
              data-testid="delete-payment-confirm"
            >
              {deletePayment.isPending ? 'Deleting...' : 'Delete'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
