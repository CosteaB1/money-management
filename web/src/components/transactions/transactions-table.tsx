'use client';

import { NotebookPen, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/src/components/ui/badge';
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/src/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/src/components/ui/table';
import { Textarea } from '@/src/components/ui/textarea';
import { useAccounts } from '@/src/lib/api/accounts';
import { useCategories } from '@/src/lib/api/categories';
import type { TransactionFilters } from '@/src/lib/api/transactions';
import {
  useDeleteTransaction,
  useTransactions,
  useUpdateTransactionCategory,
  useUpdateTransactionNotes,
} from '@/src/lib/api/transactions';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';
import type { CategoryDto, TransactionDto } from '@/src/types/api';

/**
 * Sentinel value backing the "Uncategorized" Select option. Radix Select
 * forbids an empty-string item value, so we use a non-empty token and map it
 * back to `null` (clear the category) on change.
 */
const UNCATEGORIZED_VALUE = '__uncategorized__';

interface Props {
  filters: TransactionFilters;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  emptyAction?: React.ReactNode;
  /**
   * Opt-in: render a trailing actions column with a per-row delete control.
   * Defaults to `false` so the shared /transactions list stays read-only.
   * When enabled the placeholder rows span 6 columns instead of 5.
   */
  allowDelete?: boolean;
  /**
   * Opt-in: a display-only node pinned as the last row of the table body
   * (e.g. an account's opening balance). Rendered only once data has loaded
   * — including on an empty result — and never during loading/error. The
   * table wraps it in a full-width `<TableRow>`/`<TableCell colSpan>` so the
   * caller is decoupled from the column count.
   */
  pinnedFooter?: React.ReactNode;
}

export function TransactionsTable({
  filters,
  page,
  pageSize,
  onPageChange,
  emptyAction,
  allowDelete = false,
  pinnedFooter,
}: Props) {
  const { data, isLoading, isError } = useTransactions(filters, page, pageSize);
  const categoriesQuery = useCategories({ includeArchived: true });
  // Used to look up the account currency on the rare row where the backend
  // returns a stale `currency` field — defensive, but cheap. Doubles as the
  // source for each row's account name when listing across all accounts.
  const accountsQuery = useAccounts(true);

  // When no account filter is applied (the "All accounts" case) rows can come
  // from any account, so we surface the owning account as a sub-line under the
  // description. On a single-account list it's redundant, so we omit it.
  const showAccount = !filters.accountId;

  // Column count for the empty / error / loading placeholder rows. The actions
  // column only exists when delete is enabled, so the span tracks it.
  const placeholderColSpan = allowDelete ? 6 : 5;

  const colorFor = (categoryId: string | undefined): string => {
    if (!categoryId) return '#94a3b8';
    const c = categoriesQuery.data?.find((x) => x.id === categoryId);
    return c?.color ?? '#94a3b8';
  };

  const currencyFor = (tx: TransactionDto): string => {
    if (tx.currency) return tx.currency;
    return accountsQuery.data?.find((a) => a.id === tx.accountId)?.currency ?? 'MDL';
  };

  const accountNameFor = (tx: TransactionDto): string | undefined =>
    accountsQuery.data?.find((a) => a.id === tx.accountId)?.name;

  return (
    <div className="rounded-lg border">
      <Table data-testid="transactions-table">
        <TableHeader>
          <TableRow>
            <TableHead className="w-32">Date</TableHead>
            <TableHead>Description</TableHead>
            <TableHead>Category</TableHead>
            <TableHead>Direction</TableHead>
            <TableHead className="text-right">Amount</TableHead>
            {allowDelete && (
              <TableHead className="w-12 text-right">
                <span className="sr-only">Actions</span>
              </TableHead>
            )}
          </TableRow>
        </TableHeader>
        <TableBody>
          {isError ? (
            <TableRow>
              <TableCell colSpan={placeholderColSpan} className="text-center text-destructive">
                Failed to load transactions.
              </TableCell>
            </TableRow>
          ) : isLoading || !data ? (
            <SkeletonRows allowDelete={allowDelete} />
          ) : data.items.length === 0 ? (
            <TableRow>
              <TableCell
                colSpan={placeholderColSpan}
                className="py-10 text-center text-muted-foreground"
              >
                <div className="flex flex-col items-center gap-3">
                  <p>No transactions match the current filters.</p>
                  {emptyAction}
                </div>
              </TableCell>
            </TableRow>
          ) : (
            data.items.map((tx) => (
              <TransactionRow
                key={tx.id}
                tx={tx}
                color={colorFor(tx.categoryId)}
                currency={currencyFor(tx)}
                accountName={showAccount ? accountNameFor(tx) : undefined}
                allowDelete={allowDelete}
              />
            ))
          )}
          {pinnedFooter && !isLoading && !isError && data && (
            <TableRow>
              <TableCell colSpan={placeholderColSpan} className="p-0">
                {pinnedFooter}
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
      {data && data.totalPages > 1 && (
        <div className="flex items-center justify-between border-t px-4 py-3 text-sm text-muted-foreground">
          <span>
            {data.totalCount} transaction{data.totalCount !== 1 ? 's' : ''}
          </span>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => onPageChange(data.pageNumber - 1)}
              disabled={data.pageNumber <= 1}
              className="rounded px-2 py-1 hover:bg-muted disabled:cursor-not-allowed disabled:opacity-40"
              aria-label="Previous page"
            >
              ← Prev
            </button>
            <span>
              Page {data.pageNumber} of {data.totalPages}
            </span>
            <button
              type="button"
              onClick={() => onPageChange(data.pageNumber + 1)}
              disabled={data.pageNumber >= data.totalPages}
              className="rounded px-2 py-1 hover:bg-muted disabled:cursor-not-allowed disabled:opacity-40"
              aria-label="Next page"
            >
              Next →
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function TransactionRow({
  tx,
  color,
  currency,
  accountName,
  allowDelete,
}: {
  tx: TransactionDto;
  color: string;
  currency: string;
  // Explicitly `| undefined` so the call site can pass `undefined` on a
  // single-account list under `exactOptionalPropertyTypes`.
  accountName?: string | undefined;
  allowDelete: boolean;
}) {
  const isExpense = tx.direction === 'Expense';
  const isTransfer = tx.isTransfer;
  const isAdjustment = tx.isAdjustment;
  const isInternal = isTransfer || isAdjustment;
  // Transfer and adjustment rows are visually muted because they're not
  // "real" income/expense; the colored treatment is reserved for genuine
  // flows in/out of net worth.
  const amountClass = isInternal
    ? 'text-right font-medium tabular-nums text-muted-foreground'
    : isExpense
      ? 'text-right font-medium tabular-nums text-rose-500'
      : 'text-right font-medium tabular-nums text-emerald-500';

  const showMdlEq = currency !== 'MDL';
  const sign = isExpense ? '-' : '+';

  return (
    <TableRow
      data-testid="transaction-row"
      data-transfer={isTransfer ? 'true' : 'false'}
      data-adjustment={isAdjustment ? 'true' : 'false'}
    >
      <TableCell className="text-muted-foreground">{formatShortDate(tx.transactionDate)}</TableCell>
      <TableCell className="max-w-[320px]">
        <div className="flex items-center gap-2">
          <div className="truncate font-medium" title={tx.description}>
            {tx.description}
          </div>
          {isTransfer && (
            <Badge variant="outline" data-testid="transfer-badge">
              Transfer
            </Badge>
          )}
          {isAdjustment && (
            <Badge variant="secondary" data-testid="adjustment-badge">
              Balance adjustment
            </Badge>
          )}
          <NoteControl tx={tx} />
        </div>
        {accountName && (
          <div
            className="truncate text-xs text-muted-foreground"
            data-testid="transaction-account"
            title={accountName}
          >
            {accountName}
          </div>
        )}
        {tx.notes && tx.notes.trim().length > 0 && (
          <div
            className="truncate text-xs italic text-muted-foreground"
            data-testid="transaction-note"
            title={tx.notes}
          >
            {tx.notes}
          </div>
        )}
        {tx.originalAmount !== undefined && tx.originalCurrency && (
          <div className="text-xs text-muted-foreground">
            {tx.originalAmount} {tx.originalCurrency}
          </div>
        )}
      </TableCell>
      <TableCell>
        <CategorySelect tx={tx} color={color} />
      </TableCell>
      <TableCell>
        {isExpense ? (
          <Badge variant="destructive">Expense</Badge>
        ) : (
          <Badge variant="success">Income</Badge>
        )}
      </TableCell>
      <TableCell className={amountClass}>
        <div>
          {sign}
          {formatMoney(tx.amount, currency)}
        </div>
        {showMdlEq && (
          <div className="text-xs font-normal text-muted-foreground" data-testid="tx-mdl-eq">
            {tx.amountMdl !== null ? (
              <>
                {sign}
                {formatMoney(tx.amountMdl, 'MDL')}
              </>
            ) : (
              <span
                title={`No FX rate available for ${currency} on ${tx.transactionDate}. Add one in Settings → FX rates.`}
              >
                —
              </span>
            )}
          </div>
        )}
      </TableCell>
      {allowDelete && (
        <TableCell className="text-right">
          <DeleteTransactionControl tx={tx} />
        </TableCell>
      )}
    </TableRow>
  );
}

/**
 * Returns true when a category's `flow` is assignable to a transaction with
 * the given `direction`. The backend enforces the same rule (and rejects a
 * mismatch with a 400); mirroring it client-side keeps incompatible options
 * out of the picker entirely:
 *   - Income row  → Income | Both
 *   - Expense row → Expense | Both
 */
function flowMatchesDirection(flow: CategoryDto['flow'], direction: TransactionDto['direction']) {
  if (flow === 'Both') return true;
  return flow === direction;
}

/**
 * Inline, always-on category picker for a single row — the core
 * re-categorization affordance. The trigger shows the current category
 * (colored dot + name, "Uncategorized" when none); options are the
 * non-archived categories whose flow matches the row's direction, plus an
 * "Uncategorized" sentinel that clears the category. Renders wherever the
 * shared table renders (both /transactions and the account activity list).
 *
 * Transfer/adjustment rows have no meaningful category, so the control is
 * disabled for them — re-categorizing an internal leg would be nonsensical.
 */
function CategorySelect({ tx, color }: { tx: TransactionDto; color: string }) {
  const categoriesQuery = useCategories({ includeArchived: false });
  const update = useUpdateTransactionCategory(tx.id);

  const isInternal = tx.isTransfer || tx.isAdjustment;

  const options = (categoriesQuery.data ?? []).filter((c) =>
    flowMatchesDirection(c.flow, tx.direction),
  );

  const handleChange = async (next: string) => {
    const categoryId = next === UNCATEGORIZED_VALUE ? null : next;
    // Radix fires onValueChange even when the value didn't change in some
    // edge cases; skip the round-trip when nothing actually changed.
    if (categoryId === (tx.categoryId ?? null)) return;
    try {
      await update.mutateAsync(categoryId);
      toast.success(categoryId === null ? 'Category cleared' : 'Category updated');
    } catch (err) {
      // Surface the backend 400 flow-mismatch `detail` verbatim.
      toast.error(err instanceof Error ? err.message : 'Failed to update category');
    }
  };

  return (
    <Select
      value={tx.categoryId ?? UNCATEGORIZED_VALUE}
      onValueChange={handleChange}
      disabled={isInternal || update.isPending}
    >
      <SelectTrigger
        className="h-8 w-45 border-none px-2 shadow-none hover:bg-muted focus:ring-1"
        data-testid="tx-category-select"
        aria-label="Change category"
      >
        <SelectValue>
          {tx.categoryName ? (
            <span className="flex items-center gap-2 text-sm">
              <span
                aria-hidden
                className="inline-block h-2.5 w-2.5 shrink-0 rounded-full"
                style={{ backgroundColor: color }}
              />
              <span className="truncate">{tx.categoryName}</span>
            </span>
          ) : (
            <span className="text-xs text-muted-foreground">Uncategorized</span>
          )}
        </SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem value={UNCATEGORIZED_VALUE}>
          <span className="text-muted-foreground">Uncategorized</span>
        </SelectItem>
        {options.map((c) => (
          <SelectItem key={c.id} value={c.id}>
            <span className="flex items-center gap-2">
              <span
                aria-hidden
                className="inline-block h-2.5 w-2.5 shrink-0 rounded-full"
                style={{ backgroundColor: c.color ?? '#94a3b8' }}
              />
              <span>{c.name}</span>
            </span>
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

const MAX_NOTE_LENGTH = 500;

/**
 * Ghost icon-button + small dialog for editing a single row's user-authored
 * note (free text, distinct from the bank-memo `description`). Self-contained
 * like `DeleteTransactionControl`/`CategorySelect` so the click never bubbles
 * into row-level navigation and the dialog's open state is scoped per-row.
 *
 * The textarea is seeded with the current note; clearing it and saving sends
 * an empty string to the notes endpoint (the backend treats null/blank as
 * "clear"). Works in both `/transactions` and the account Activity list — the
 * affordance lives in the Description cell, not the (optional) actions column,
 * so it renders regardless of `allowDelete`.
 */
function NoteControl({ tx }: { tx: TransactionDto }) {
  const [open, setOpen] = useState(false);
  const [value, setValue] = useState(tx.notes ?? '');
  const update = useUpdateTransactionNotes(tx.id);

  const hasNote = Boolean(tx.notes && tx.notes.trim().length > 0);

  // Re-seed the textarea from the current note each time the dialog opens so a
  // cancelled edit (or a note changed elsewhere) never leaves stale text.
  const handleOpenChange = (next: boolean) => {
    if (next) setValue(tx.notes ?? '');
    setOpen(next);
  };

  const handleSave = async () => {
    const trimmed = value.trim();
    try {
      await update.mutateAsync(trimmed.length === 0 ? null : trimmed);
      toast.success(trimmed.length === 0 ? 'Note cleared' : 'Note saved');
      setOpen(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to save note');
    }
  };

  return (
    <>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="h-6 w-6 shrink-0 text-muted-foreground hover:text-foreground"
        aria-label={hasNote ? 'Edit note' : 'Add note'}
        data-testid={`edit-note-${tx.id}`}
        onClick={(e) => {
          e.stopPropagation();
          handleOpenChange(true);
        }}
      >
        <NotebookPen className="h-3.5 w-3.5" aria-hidden />
      </Button>
      <Dialog open={open} onOpenChange={handleOpenChange}>
        <DialogContent data-testid="note-dialog">
          <DialogHeader>
            <DialogTitle>{hasNote ? 'Edit note' : 'Add note'}</DialogTitle>
            <DialogDescription>
              A free-text note for <strong>{tx.description}</strong>. Clear it to remove the note.
            </DialogDescription>
          </DialogHeader>
          <Textarea
            data-testid="note-textarea"
            aria-label="Note"
            maxLength={MAX_NOTE_LENGTH}
            rows={4}
            value={value}
            onChange={(e) => setValue(e.target.value)}
          />
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button
              type="button"
              disabled={update.isPending}
              onClick={handleSave}
              data-testid="note-save"
            >
              {update.isPending ? 'Saving...' : 'Save'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

/**
 * Ghost icon-button + destructive confirm dialog for a single row. Kept
 * self-contained so the click never bubbles into row-level navigation and so
 * the dialog's open state is scoped per-row (no shared "which row?" plumbing).
 */
function DeleteTransactionControl({ tx }: { tx: TransactionDto }) {
  const [open, setOpen] = useState(false);
  const deleteTransaction = useDeleteTransaction();

  const handleConfirm = async () => {
    try {
      await deleteTransaction.mutateAsync(tx.id);
      toast.success('Transaction deleted');
      setOpen(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to delete transaction');
    }
  };

  return (
    <>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="h-8 w-8 text-muted-foreground hover:text-destructive"
        aria-label="Delete transaction"
        data-testid="delete-transaction"
        onClick={(e) => {
          e.stopPropagation();
          setOpen(true);
        }}
      >
        <Trash2 aria-hidden />
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent data-testid="delete-transaction-dialog">
          <DialogHeader>
            <DialogTitle>Delete transaction?</DialogTitle>
            <DialogDescription>
              This removes <strong>{tx.description}</strong> from this account&apos;s activity. The
              account balance and performance figures will be recalculated.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              disabled={deleteTransaction.isPending}
              onClick={handleConfirm}
              data-testid="delete-transaction-confirm"
            >
              {deleteTransaction.isPending ? 'Deleting...' : 'Delete'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

const SKELETON_ROW_IDS = ['r1', 'r2', 'r3', 'r4', 'r5', 'r6'] as const;
const SKELETON_CELL_IDS = ['c1', 'c2', 'c3', 'c4', 'c5'] as const;

function SkeletonRows({ allowDelete }: { allowDelete: boolean }) {
  const cellIds = allowDelete ? [...SKELETON_CELL_IDS, 'c6'] : SKELETON_CELL_IDS;
  return (
    <>
      {SKELETON_ROW_IDS.map((rowId) => (
        <TableRow key={rowId}>
          {cellIds.map((cellId) => (
            <TableCell key={`${rowId}-${cellId}`}>
              <div className="h-4 w-full max-w-40 animate-pulse rounded bg-muted" />
            </TableCell>
          ))}
        </TableRow>
      ))}
    </>
  );
}
