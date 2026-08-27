'use client';

import { ArrowLeft, HandCoins, Pencil, Trash2 } from 'lucide-react';
import Link from 'next/link';
import { useState } from 'react';
import { toast } from 'sonner';
import { ArchiveLoanDialog } from '@/src/components/loans/archive-loan-dialog';
import { EditLoanDialog } from '@/src/components/loans/edit-loan-dialog';
import { RecordPaymentDialog } from '@/src/components/loans/record-payment-dialog';
import { Badge } from '@/src/components/ui/badge';
import { Button } from '@/src/components/ui/button';
import { useUnarchiveLoan } from '@/src/lib/api/loans';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';
import type { LoanDetailDto, LoanDto } from '@/src/types/api';

interface Props {
  loan: LoanDetailDto;
}

/**
 * Top strip of the detail page: back link, counterparty, principal +
 * direction + status badges (plus Archived when archived), loan-date
 * subtitle, and the action button group. On archived loans the mutating
 * actions are replaced by a single Unarchive button (mirrors the account
 * detail header); "Record payment" is additionally gated to non-settled
 * loans (recording against a settled loan would always fail the
 * outstanding-max validation).
 *
 * The action buttons reuse the same dialogs the list page uses
 * (`RecordPaymentDialog`, `EditLoanDialog`, `ArchiveLoanDialog`), driven
 * from local state instead of a row menu.
 */
export function LoanDetailHeader({ loan }: Props) {
  // Shape the detail DTO into the lighter LoanDto the child dialogs
  // expect — they were designed around the list endpoint's shape, and we
  // don't want to leak the detail-only fields (payments, disbursement
  // link, createdOn) into their props.
  const loanForDialogs: LoanDto = {
    id: loan.id,
    direction: loan.direction,
    counterparty: loan.counterparty,
    principal: loan.principal,
    currency: loan.currency,
    loanDate: loan.loanDate,
    totalRepaid: loan.totalRepaid,
    outstanding: loan.outstanding,
    outstandingMdl: loan.outstandingMdl,
    missingFxRate: loan.missingFxRate,
    status: loan.status,
    paymentCount: loan.paymentCount,
    notes: loan.notes,
    isArchived: loan.isArchived,
  };

  const unarchive = useUnarchiveLoan();
  const [recordOpen, setRecordOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const [archiveOpen, setArchiveOpen] = useState(false);

  const handleUnarchive = async () => {
    try {
      await unarchive.mutateAsync(loan.id);
      toast.success(`Unarchived "${loan.counterparty}" loan`);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to unarchive loan');
    }
  };

  return (
    <div className="space-y-4" data-testid="loan-detail-header">
      <div>
        <Link
          href="/loans"
          aria-label="Back to loans"
          className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
          data-testid="loan-detail-back"
        >
          <ArrowLeft className="h-4 w-4" />
          Loans
        </Link>
      </div>

      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <h1
              className="text-2xl font-semibold tracking-tight"
              data-testid="loan-detail-counterparty"
            >
              {loan.counterparty}
            </h1>
            <Badge variant="secondary" data-testid="loan-detail-principal">
              {formatMoney(loan.principal, loan.currency)}
            </Badge>
            <Badge
              variant={loan.direction === 'Received' ? 'secondary' : 'outline'}
              data-testid="loan-detail-direction"
            >
              {loan.direction === 'Received' ? 'Borrowed' : 'Lent'}
            </Badge>
            <Badge
              variant={loan.status === 'Active' ? 'success' : 'outline'}
              data-testid="loan-detail-status"
            >
              {loan.status}
            </Badge>
            {loan.isArchived && (
              <Badge variant="outline" data-testid="loan-detail-archived">
                Archived
              </Badge>
            )}
          </div>
          <p className="text-sm text-muted-foreground" data-testid="loan-detail-loan-date">
            Loan date {formatShortDate(loan.loanDate)}
          </p>
        </div>

        {!loan.isArchived && (
          <div className="flex flex-wrap items-center gap-2" data-testid="loan-detail-actions">
            {loan.status !== 'Settled' && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => setRecordOpen(true)}
                data-testid="loan-detail-record-payment"
              >
                <HandCoins className="h-4 w-4" />
                Record payment
              </Button>
            )}
            <Button
              variant="outline"
              size="sm"
              onClick={() => setEditOpen(true)}
              data-testid="loan-detail-edit"
            >
              <Pencil className="h-4 w-4" />
              Edit loan
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setArchiveOpen(true)}
              data-testid="loan-detail-archive"
            >
              <Trash2 className="h-4 w-4" />
              Archive
            </Button>
          </div>
        )}

        {/* Archived loans swap the whole action group for a single
            Unarchive button — mirrors the account detail header. */}
        {loan.isArchived && (
          <div className="flex flex-wrap items-center gap-2" data-testid="loan-detail-actions">
            <Button
              variant="ghost"
              size="sm"
              onClick={handleUnarchive}
              disabled={unarchive.isPending}
              data-testid="loan-detail-unarchive"
            >
              Unarchive
            </Button>
          </div>
        )}
      </div>

      {/* Controlled instances of the list-page dialogs — the trigger
          elements live in the action group above. Skipped entirely on
          archived loans, whose actions are hidden. */}
      {!loan.isArchived && (
        <>
          {loan.status !== 'Settled' && (
            <RecordPaymentDialog
              loan={loanForDialogs}
              open={recordOpen}
              onOpenChange={setRecordOpen}
            />
          )}
          <EditLoanDialog loan={loanForDialogs} open={editOpen} onOpenChange={setEditOpen} />
          <ArchiveLoanDialog
            loan={loanForDialogs}
            open={archiveOpen}
            onOpenChange={setArchiveOpen}
          />
        </>
      )}
    </div>
  );
}
