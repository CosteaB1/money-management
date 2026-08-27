'use client';

import { AlertTriangle, MoreHorizontal } from 'lucide-react';
import Link from 'next/link';
import { useState } from 'react';
import { toast } from 'sonner';
import { ArchiveLoanDialog } from '@/src/components/loans/archive-loan-dialog';
import { EditLoanDialog } from '@/src/components/loans/edit-loan-dialog';
import { RecordPaymentDialog } from '@/src/components/loans/record-payment-dialog';
import { Badge } from '@/src/components/ui/badge';
import { Button } from '@/src/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/src/components/ui/dropdown-menu';
import { Label } from '@/src/components/ui/label';
import { Switch } from '@/src/components/ui/switch';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/src/components/ui/table';
import { useLoans, useUnarchiveLoan } from '@/src/lib/api/loans';
import { cn } from '@/src/lib/utils/cn';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';
import type { LoanDirection, LoanDto, LoanStatus } from '@/src/types/api';

// Repayment progress caps at 100% — payments can never exceed the
// principal (the record-payment dialog and the backend both clamp).
const PROGRESS_CAP = 1;

const DIRECTION_BADGE: Record<LoanDirection, { variant: 'secondary' | 'outline'; label: string }> =
  {
    // 'Received' = money the user borrowed — they owe it back.
    Received: { variant: 'secondary', label: 'Borrowed' },
    // 'Given' = money the user lent out — it's owed back to them.
    Given: { variant: 'outline', label: 'Lent' },
  };

const STATUS_BADGE: Record<LoanStatus, { variant: 'success' | 'outline'; label: string }> = {
  Active: { variant: 'success', label: 'Active' },
  Settled: { variant: 'outline', label: 'Settled' },
};

export function LoansTable() {
  const [includeArchived, setIncludeArchived] = useState(false);
  const { data, isLoading, isError } = useLoans(includeArchived);
  const unarchive = useUnarchiveLoan();
  const [recordTarget, setRecordTarget] = useState<LoanDto | null>(null);
  const [editTarget, setEditTarget] = useState<LoanDto | null>(null);
  const [archiveTarget, setArchiveTarget] = useState<LoanDto | null>(null);

  const handleUnarchive = async (id: string, counterparty: string) => {
    try {
      await unarchive.mutateAsync(id);
      toast.success(`Unarchived "${counterparty}" loan`);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to unarchive loan');
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-end gap-2">
        <Label htmlFor="show-archived" className="text-sm text-muted-foreground">
          Show archived
        </Label>
        <Switch
          id="show-archived"
          data-testid="show-archived-toggle"
          checked={includeArchived}
          onCheckedChange={setIncludeArchived}
        />
      </div>

      <div className="rounded-lg border">
        <Table data-testid="loans-table">
        <TableHeader>
          <TableRow>
            <TableHead>Counterparty</TableHead>
            <TableHead>Direction</TableHead>
            <TableHead className="text-right">Principal</TableHead>
            <TableHead className="text-right">Repaid</TableHead>
            <TableHead className="text-right">Outstanding</TableHead>
            <TableHead className="w-[180px]">Progress</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Loan date</TableHead>
            <TableHead className="w-12 text-right">
              <span className="sr-only">Actions</span>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isError ? (
            <TableRow>
              <TableCell colSpan={9} className="text-center text-destructive">
                Failed to load loans.
              </TableCell>
            </TableRow>
          ) : isLoading || !data ? (
            <LoansSkeletonRows />
          ) : data.length === 0 ? (
            <TableRow>
              <TableCell colSpan={9} className="py-10 text-center text-muted-foreground">
                No loans yet — click &ldquo;Add loan&rdquo; to track money you borrowed or lent.
              </TableCell>
            </TableRow>
          ) : (
            data.map((loan) => (
              <LoanRow
                key={loan.id}
                loan={loan}
                onRecordPayment={() => setRecordTarget(loan)}
                onEdit={() => setEditTarget(loan)}
                onArchive={() => setArchiveTarget(loan)}
                onUnarchive={() => handleUnarchive(loan.id, loan.counterparty)}
              />
            ))
          )}
        </TableBody>
        </Table>
      </div>

      {recordTarget && (
        <RecordPaymentDialog
          loan={recordTarget}
          open={recordTarget !== null}
          onOpenChange={(next) => {
            if (!next) setRecordTarget(null);
          }}
        />
      )}
      {editTarget && (
        <EditLoanDialog
          loan={editTarget}
          open={editTarget !== null}
          onOpenChange={(next) => {
            if (!next) setEditTarget(null);
          }}
        />
      )}
      {archiveTarget && (
        <ArchiveLoanDialog
          loan={archiveTarget}
          open={archiveTarget !== null}
          onOpenChange={(next) => {
            if (!next) setArchiveTarget(null);
          }}
        />
      )}
    </div>
  );
}

function LoanRow({
  loan,
  onRecordPayment,
  onEdit,
  onArchive,
  onUnarchive,
}: {
  loan: LoanDto;
  onRecordPayment: () => void;
  onEdit: () => void;
  onArchive: () => void;
  onUnarchive: () => void;
}) {
  const ratio = loan.totalRepaid / loan.principal;
  const cappedRatio = Math.min(Math.max(ratio, 0), PROGRESS_CAP);
  const widthPct = `${cappedRatio * 100}%`;
  const directionBadge = DIRECTION_BADGE[loan.direction];
  const statusBadge = STATUS_BADGE[loan.status];

  return (
    <TableRow data-testid="loan-row" data-direction={loan.direction} data-status={loan.status}>
      <TableCell className="font-medium">
        {/* Only the counterparty cell is a link — keeps the row-action
            dropdown's pointer events isolated so clicking the menu trigger
            never navigates. Mirrors the goals-table pattern. */}
        <Link
          href={`/loans/${loan.id}`}
          className="rounded-sm underline-offset-4 hover:text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
          data-testid="loan-counterparty-link"
        >
          {loan.counterparty}
        </Link>
      </TableCell>
      <TableCell>
        <Badge variant={directionBadge.variant} data-testid="loan-direction-badge">
          {directionBadge.label}
        </Badge>
      </TableCell>
      <TableCell className="text-right tabular-nums" data-testid="loan-principal">
        {formatMoney(loan.principal, loan.currency)}
      </TableCell>
      <TableCell
        className="text-right tabular-nums text-muted-foreground"
        data-testid="loan-repaid"
      >
        {formatMoney(loan.totalRepaid, loan.currency)}
      </TableCell>
      <TableCell className="text-right tabular-nums">
        <span
          className="inline-flex items-center justify-end gap-1.5"
          data-testid="loan-outstanding"
        >
          {formatMoney(loan.outstanding, loan.currency)}
          {loan.missingFxRate && (
            <span
              title="No FX rate available to convert to MDL"
              role="img"
              aria-label="No FX rate available to convert to MDL"
              data-testid="loan-missing-fx-icon"
              className="inline-flex"
            >
              <AlertTriangle className="h-3.5 w-3.5 text-amber-500" aria-hidden />
            </span>
          )}
        </span>
        {loan.currency !== 'MDL' && loan.outstandingMdl !== null && (
          <div
            className="text-xs font-normal text-muted-foreground"
            data-testid="loan-outstanding-mdl"
          >
            {formatMoney(loan.outstandingMdl, 'MDL')}
          </div>
        )}
      </TableCell>
      <TableCell>
        <div
          className="h-1.5 w-full overflow-hidden rounded-full bg-muted"
          role="progressbar"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(cappedRatio * 100)}
          aria-label={`${loan.counterparty} loan repaid ${Math.round(cappedRatio * 100)} percent`}
        >
          <div
            data-testid="loan-progress-bar"
            data-status={loan.status}
            className={cn(
              'h-full bg-emerald-500 transition-[width]',
              loan.status === 'Settled' && 'opacity-80 ring-1 ring-inset ring-emerald-400/60',
            )}
            style={{ width: widthPct }}
          />
        </div>
      </TableCell>
      <TableCell>
        <span className="inline-flex flex-wrap items-center gap-1.5">
          <Badge variant={statusBadge.variant} data-testid="loan-status-pill">
            {statusBadge.label}
          </Badge>
          {/* Same outline treatment the loan detail header uses. */}
          {loan.isArchived && (
            <Badge variant="outline" data-testid="loan-archived-badge">
              Archived
            </Badge>
          )}
        </span>
      </TableCell>
      <TableCell className="text-muted-foreground" data-testid="loan-date">
        {formatShortDate(loan.loanDate)}
      </TableCell>
      <TableCell className="text-right">
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              variant="ghost"
              size="icon"
              aria-label={`Actions for ${loan.counterparty} loan`}
              data-testid="loan-actions"
            >
              <MoreHorizontal className="h-4 w-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            {loan.isArchived ? (
              // Archived rows expose a single action — Unarchive. Mutating
              // actions stay hidden until the loan is active again
              // (mirrors the accounts table).
              <DropdownMenuItem onClick={onUnarchive} data-testid="unarchive-loan-action">
                Unarchive
              </DropdownMenuItem>
            ) : (
              <>
                {/* Recording against a settled loan would always fail the
                    outstanding-max validation, so the action is gated out. */}
                {loan.status !== 'Settled' && (
                  <DropdownMenuItem onClick={onRecordPayment} data-testid="record-payment-action">
                    Record payment
                  </DropdownMenuItem>
                )}
                <DropdownMenuItem onClick={onEdit} data-testid="edit-loan-action">
                  Edit
                </DropdownMenuItem>
                <DropdownMenuItem onClick={onArchive} data-testid="archive-loan-action">
                  Archive
                </DropdownMenuItem>
              </>
            )}
          </DropdownMenuContent>
        </DropdownMenu>
      </TableCell>
    </TableRow>
  );
}

const SKELETON_ROW_IDS = ['s1', 's2', 's3'] as const;
const SKELETON_CELL_IDS = ['c1', 'c2', 'c3', 'c4', 'c5', 'c6', 'c7', 'c8', 'c9'] as const;

function LoansSkeletonRows() {
  return (
    <>
      {SKELETON_ROW_IDS.map((rowId) => (
        <TableRow key={rowId}>
          {SKELETON_CELL_IDS.map((cellId) => (
            <TableCell key={`${rowId}-${cellId}`}>
              <div className="h-4 w-full max-w-[160px] animate-pulse rounded bg-muted" />
            </TableCell>
          ))}
        </TableRow>
      ))}
    </>
  );
}
