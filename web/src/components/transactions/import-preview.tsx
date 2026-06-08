'use client';

import { Building2, StickyNote } from 'lucide-react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { type Dispatch, memo, useCallback, useMemo, useReducer, useState } from 'react';
import { toast } from 'sonner';
import { CreateCategoryDialog } from '@/src/components/categories/create-category-dialog';
import { Badge } from '@/src/components/ui/badge';
import { Button } from '@/src/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { Checkbox } from '@/src/components/ui/checkbox';
import { Input } from '@/src/components/ui/input';
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
import { useAccounts } from '@/src/lib/api/accounts';
import { useCategories } from '@/src/lib/api/categories';
import { convertFx } from '@/src/lib/api/fx-rates';
import { useCommitImport } from '@/src/lib/api/imports';
import { formatEffectiveRate, formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';
import { proposeKeyword } from '@/src/lib/utils/propose-keyword';
import type {
  AccountDto,
  CategoryDto,
  CommitImportRequest,
  ParsedTransactionPreview,
  StatementPreviewDto,
} from '@/src/types/api';

interface RowSelection {
  included: boolean;
  categoryId: string;
  isTransfer: boolean;
  /**
   * Optional counter account. When set on a transfer row, the backend writes
   * a matching leg on this account (ATM → Cash, Salary → Brokerage). When
   * left null, the row is imported as a single leg — the canonical case for
   * MAIB → MAIB A2A where the counter side has its own PDF.
   */
  counterAccountId: string | null;
  /**
   * Amount received on the counter account, in the counter account's native
   * currency, as a raw input string ('' when unset). Only relevant when the
   * counter account's currency differs from the import account's. Seeded from
   * an FX conversion when the user picks a different-currency counter, unless
   * `counterAmountDirty` is set (the user typed their own value).
   */
  counterAmount: string;
  counterAmountDirty: boolean;
  /**
   * Learn-with-confirm state. When the user assigns a category the suggester
   * did NOT already match, we propose persisting a keyword → category rule so
   * the NEXT import auto-suggests it. `learnEnabled` gates the per-row UI and
   * whether the rule ships in the commit; `learnKeyword` is the editable
   * candidate seeded from `proposeKeyword(description)`.
   */
  learnEnabled: boolean;
  learnKeyword: string;
  /**
   * Optional free-text note the user attaches to this row during import. Lives
   * in row state so it survives re-renders, but only ships in the commit payload
   * when non-blank (see `handleCommit`'s conditional spread). Revealed on demand
   * via a small affordance in the Description cell — never an always-visible
   * column, to keep the dense import table readable.
   */
  notes: string;
}

type SelectionsState = RowSelection[];

type SelectionsAction =
  | { type: 'init'; preview: StatementPreviewDto }
  | { type: 'toggle'; index: number; included: boolean }
  | {
      type: 'set-category';
      index: number;
      categoryId: string;
      // The suggester's pick + raw memo travel with the action so the reducer
      // can decide whether to propose a learn rule (override / miss) and seed
      // the keyword candidate without reaching into component state.
      suggestedCategoryId: string;
      description: string;
    }
  | { type: 'set-learn-keyword'; index: number; keyword: string }
  | { type: 'dismiss-learn'; index: number }
  | { type: 'set-notes'; index: number; notes: string }
  | { type: 'toggle-transfer'; index: number; isTransfer: boolean }
  | { type: 'set-counter-account'; index: number; counterAccountId: string | null }
  | { type: 'set-counter-amount'; index: number; counterAmount: string }
  | { type: 'include-all'; preview: StatementPreviewDto }
  | { type: 'exclude-duplicates'; preview: StatementPreviewDto }
  | { type: 'reset'; preview: StatementPreviewDto };

const UNCATEGORIZED = '__uncategorized__';
const NONE_COUNTER = '__none__';
// Sentinel option appended to every per-row category picker. Selecting it does
// NOT categorize the row — it opens the shared CreateCategoryDialog so the user
// can mint a new category without leaving the import flow. See the row Select's
// `onValueChange` below for the guard that keeps this value from ever landing
// in the reducer.
const NEW_CATEGORY = '__new_category__';

function initial(preview: StatementPreviewDto): SelectionsState {
  return preview.transactions.map((t) => ({
    included: !t.isDuplicate,
    categoryId: t.suggestedCategoryId ?? UNCATEGORIZED,
    // Seed from the backend's auto-suggested transfer flag; user can toggle below.
    // Marking a row as transfer always excludes it from income/expense aggregates.
    // A counter account is *optional*: when supplied the backend mirrors the leg
    // onto the picked account (ATM → Cash, Salary → XTB/Binance/Fagura). When
    // left blank, the row imports as-is — the case for MAIB → MAIB A2A where the
    // other PDF carries its own side.
    isTransfer: t.isTransfer,
    counterAccountId: null,
    // Cross-currency received amount: blank until the user picks a different-
    // currency counter account (which triggers an FX pre-fill in the
    // component). `counterAmountDirty` guards a user-typed value from being
    // overwritten by a later pre-fill.
    counterAmount: '',
    counterAmountDirty: false,
    // Learning is opt-in per row and only switches on once the user picks a
    // category the suggester missed (see the `set-category` reducer case).
    learnEnabled: false,
    learnKeyword: '',
    // Per-row note starts blank; revealed on demand and only shipped when typed.
    notes: '',
  }));
}

function reducer(state: SelectionsState, action: SelectionsAction): SelectionsState {
  switch (action.type) {
    // `init` is never dispatched: useReducer seeds state via the lazy `initial`
    // initializer, not an init action.
    /* v8 ignore start */
    case 'init':
      return initial(action.preview);
    /* v8 ignore stop */
    case 'toggle':
      return state.map((row, idx) =>
        idx === action.index ? { ...row, included: action.included } : row,
      );
    case 'set-category':
      return state.map((row, idx) => {
        if (idx !== action.index) return row;
        const isReal = action.categoryId !== UNCATEGORIZED;
        const suggested = action.suggestedCategoryId || UNCATEGORIZED;
        // Propose learning only when the user lands on a REAL category that the
        // suggester did NOT already match (a miss or an override). Picking the
        // exact suggestion means the rule already exists — nothing to learn.
        // Going back to Uncategorized always disables learning for the row.
        const shouldLearn = isReal && action.categoryId !== suggested;
        if (!shouldLearn) {
          return { ...row, categoryId: action.categoryId, learnEnabled: false };
        }
        // Re-seed the keyword on each qualifying pick so switching the category
        // refreshes the proposal, but keep a keyword the user already edited if
        // learning was already on for this row.
        const keyword =
          row.learnEnabled && row.learnKeyword
            ? row.learnKeyword
            : proposeKeyword(action.description);
        return {
          ...row,
          categoryId: action.categoryId,
          learnEnabled: true,
          learnKeyword: keyword,
        };
      });
    case 'set-learn-keyword':
      return state.map((row, idx) =>
        idx === action.index ? { ...row, learnKeyword: action.keyword } : row,
      );
    case 'dismiss-learn':
      return state.map((row, idx) =>
        idx === action.index ? { ...row, learnEnabled: false } : row,
      );
    case 'set-notes':
      // Mirrors `set-learn-keyword`: store the raw textarea value verbatim. The
      // omit-when-blank rule is applied at commit time, not here, so the user
      // can clear a note back to empty and re-type freely.
      return state.map((row, idx) =>
        idx === action.index ? { ...row, notes: action.notes } : row,
      );
    case 'toggle-transfer':
      return state.map((row, idx) =>
        idx === action.index
          ? {
              ...row,
              isTransfer: action.isTransfer,
              // Unchecking transfer abandons any counter pick — the row is no
              // longer an internal movement, so the counter field is meaningless.
              counterAccountId: action.isTransfer ? row.counterAccountId : null,
              // Also wipe the received amount: it's only meaningful for a
              // cross-currency counter pick, which no longer exists.
              counterAmount: action.isTransfer ? row.counterAmount : '',
              counterAmountDirty: action.isTransfer ? row.counterAmountDirty : false,
            }
          : row,
      );
    case 'set-counter-account':
      // Changing the counter account always resets the received amount + its
      // dirty flag so a stale (different-currency) value can't ride along. The
      // component re-seeds it via an imperative `convertFx` → `set-counter-amount`
      // when the new account's currency differs from the import currency.
      return state.map((row, idx) =>
        idx === action.index
          ? {
              ...row,
              counterAccountId: action.counterAccountId,
              counterAmount: '',
              counterAmountDirty: false,
            }
          : row,
      );
    case 'set-counter-amount':
      return state.map((row, idx) =>
        idx === action.index
          ? { ...row, counterAmount: action.counterAmount, counterAmountDirty: true }
          : row,
      );
    case 'include-all':
      return state.map((row) => ({ ...row, included: true }));
    case 'exclude-duplicates':
      return state.map((row, idx) => ({
        ...row,
        included: action.preview.transactions[idx]?.isDuplicate ? false : row.included,
      }));
    case 'reset':
      return initial(action.preview);
    // Exhaustive switch over a typed union; the default is an unreachable net.
    /* v8 ignore start */
    default:
      return state;
    /* v8 ignore stop */
  }
}

interface Props {
  preview: StatementPreviewDto;
  accountId: string;
  fileName: string;
  onCancel: () => void;
}

export function ImportPreview({ preview, accountId, fileName, onCancel }: Props) {
  const router = useRouter();
  const categoriesQuery = useCategories({ includeArchived: false });
  const accountsQuery = useAccounts(true);
  const commit = useCommitImport();
  const [selections, dispatch] = useReducer(reducer, preview, initial);

  // Inline category creation. When a row's picker selects the NEW_CATEGORY
  // sentinel we stash that row's index here and open the shared dialog; on a
  // successful create we assign the new id back to exactly that row. `null`
  // means the dialog is closed. We key the create dialog on this index so its
  // `defaultFlow` re-seeds to the triggering row's direction each time.
  const [newCategoryRow, setNewCategoryRow] = useState<number | null>(null);

  // Ephemeral UI: which rows have their note editor toggled open. This is purely
  // "did the user click the add-note affordance" — it does NOT hold the note text
  // (that lives in the reducer's RowSelection.notes). The textarea is shown when
  // EITHER the row is in this set OR the row already has a non-blank note, so a
  // typed note stays visible/editable even across a toggle. Plain useState<Set>
  // matches the component's pattern of useReducer for row data + useState for
  // throwaway UI bits.
  const [openNoteRows, setOpenNoteRows] = useState<Set<number>>(new Set());
  // Stable across renders so passing it to a memoized row doesn't bust the memo.
  // The functional set update reads `prev`, so this callback never needs to
  // close over the current `openNoteRows` value.
  const toggleNoteRow = useCallback(
    (idx: number) =>
      setOpenNoteRows((prev) => {
        const next = new Set(prev);
        if (next.has(idx)) next.delete(idx);
        else next.add(idx);
        return next;
      }),
    [],
  );

  // The import account dictates the currency we display amounts in. maib
  // PDFs are MDL today, but routing this through `formatMoney` keeps us
  // forward-compatible with future banks (e.g. Revolut EUR exports).
  const accountCurrency = accountsQuery.data?.find((a) => a.id === accountId)?.currency ?? 'MDL';

  // Eligible counter accounts: any non-archived account other than the import
  // account itself. Cross-currency counters are allowed — when the picked
  // account's currency differs from the import currency, the row reveals an
  // "Amount received" field (see the row UI below). Deliberately *not*
  // auto-preselected — the user knows whether the other side already has a
  // PDF coming. Empty list ⇒ render the picker disabled silently.
  const counterAccountOptions = useMemo(() => {
    const all = accountsQuery.data ?? [];
    return all.filter((a) => !a.isArchived && a.id !== accountId);
  }, [accountsQuery.data, accountId]);

  // Currency lookup keyed by account id, used to decide whether a picked
  // counter account is cross-currency (and thus needs a received amount).
  const currencyByAccountId = useMemo(() => {
    const map = new Map<string, string>();
    for (const a of accountsQuery.data ?? []) map.set(a.id, a.currency);
    return map;
  }, [accountsQuery.data]);

  // Name lookup for the learn-rule hint ("…as {categoryName}"). Built once per
  // category load rather than scanning the array per row render. Passed to each
  // row as a stable Map so the row can resolve a category name without a new
  // closure per parent render.
  const categoryNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const c of categoriesQuery.data ?? []) map.set(c.id, c.name);
    return map;
  }, [categoriesQuery.data]);

  // The full category list, referentially stable across parent re-renders so it
  // can feed each memoized row. The row derives its own per-direction
  // `flowOptions` from this array (the filter used to live inline in the map,
  // allocating a fresh array per row on every parent render — see ImportPreviewRow).
  const categories = useMemo(() => categoriesQuery.data ?? [], [categoriesQuery.data]);

  const counters = useMemo(() => {
    let count = 0;
    let income = 0;
    let expense = 0;
    selections.forEach((sel, idx) => {
      if (!sel.included) return;
      const row = preview.transactions[idx];
      if (!row) return;
      count++;
      // Include transfers here: this counter is the total of what you're
      // importing, so it should match the statement's Total intrări / ieșiri
      // (those rows ARE imported and DO move the account balance). The
      // "real income vs spending excluding transfers" lens lives on the
      // dashboard/reports, not on the import action total.
      if (row.direction === 'Income') income += row.amount;
      else expense += row.amount;
    });
    return { count, income, expense };
  }, [selections, preview.transactions]);

  // Row-derived verification: recompute totals straight from ALL parsed rows
  // (not just the included ones) so the user can confirm the parse matches the
  // printed PDF header. `rowFees` sums the parser's commission rows (those whose
  // description starts with "Comision:") — a SUBSET of Out, never subtracted
  // separately. Each value is compared to its header counterpart with the same
  // float tolerance used for the closing-balance reconciliation.
  const rowCheck = useMemo(() => {
    let rowIn = 0;
    let rowOut = 0;
    let rowFees = 0;
    for (const row of preview.transactions) {
      if (row.direction === 'Income') rowIn += row.amount;
      else rowOut += row.amount;
      if (row.description.startsWith('Comision:')) rowFees += row.amount;
    }
    const tolerance = 0.005;
    return {
      rowIn,
      rowOut,
      rowFees,
      inMatches: Math.abs(rowIn - preview.summary.totalIn) < tolerance,
      outMatches: Math.abs(rowOut - preview.summary.totalOut) < tolerance,
      feesMatches: Math.abs(rowFees - preview.summary.totalFees) < tolerance,
      balance: preview.summary.openingBalance + rowIn - rowOut,
    };
  }, [preview.transactions, preview.summary]);

  // Picking a counter account: reset the prior received-amount (reducer does
  // this), then — when the new account is a DIFFERENT currency — kick off an
  // imperative FX conversion and seed the received amount from it. Kept out of
  // the reducer so the reducer stays pure (async + network live here).
  //
  // Wrapped in useCallback so its identity is stable across parent re-renders;
  // a memoized row receives the same reference every render and never re-renders
  // on this prop alone. Its dependencies (`currencyByAccountId`, `accountCurrency`,
  // `preview.transactions`) are themselves stable per data load.
  const handleSelectCounterAccount = useCallback(
    (idx: number, counterAccountId: string | null) => {
      dispatch({ type: 'set-counter-account', index: idx, counterAccountId });
      if (!counterAccountId) return;
      const counterCcy = currencyByAccountId.get(counterAccountId);
      if (!counterCcy || counterCcy === accountCurrency) return;
      const row = preview.transactions[idx];
      if (!row || !(row.amount > 0)) return;
      convertFx({
        from: accountCurrency,
        to: counterCcy,
        date: row.transactionDate,
        amount: row.amount,
      })
        .then((res) => {
          // `hasRate` false ⇒ leave blank for the user to type the received amount.
          if (res.hasRate && res.convertedAmount !== null) {
            dispatch({
              type: 'set-counter-amount',
              index: idx,
              counterAmount: res.convertedAmount.toFixed(2),
            });
          }
        })
        .catch(() => {
          // A failed lookup just leaves the field blank — the user enters the
          // received amount manually.
        });
    },
    [currencyByAccountId, accountCurrency, preview.transactions],
  );

  // Stable opener for the inline create-category dialog. The row's picker calls
  // this with its own index when the NEW_CATEGORY sentinel is chosen; the parent
  // owns the dialog state so toggling it doesn't reflow every row's props.
  const handleRequestNewCategory = useCallback((idx: number) => {
    setNewCategoryRow(idx);
  }, []);

  const handleCommit = async () => {
    // Guard: a cross-currency transfer row (included, transfer, counter account
    // in a different currency) must carry a positive received amount. We can't
    // express this in the payload `.map` alone, so check up front and point the
    // user at the offending row.
    for (let idx = 0; idx < preview.transactions.length; idx++) {
      const sel = selections[idx];
      const row = preview.transactions[idx];
      if (!sel?.included || !row || !sel.isTransfer || !sel.counterAccountId) continue;
      const counterCcy = currencyByAccountId.get(sel.counterAccountId);
      if (!counterCcy || counterCcy === accountCurrency) continue;
      const received = Number(sel.counterAmount);
      if (sel.counterAmount.trim().length === 0 || !Number.isFinite(received) || received <= 0) {
        toast.error(`Enter the amount received for row ${idx + 1} (${row.description}).`);
        return;
      }
    }

    const payload: CommitImportRequest = {
      accountId,
      fileName,
      fileHash: preview.fileHash,
      bankSource: preview.bankSource,
      transactions: preview.transactions
        .map((row, idx) => {
          const sel = selections[idx];
          if (!sel?.included) return null;
          // A cross-currency transfer row carries the received amount in the
          // counter account's currency; same-currency rows omit it (backend
          // defaults it to `amount`).
          const counterCcy = sel.counterAccountId
            ? currencyByAccountId.get(sel.counterAccountId)
            : undefined;
          const crossCurrencyCounter =
            sel.isTransfer && Boolean(counterCcy) && counterCcy !== accountCurrency;
          return {
            transactionDate: row.transactionDate,
            direction: row.direction,
            amount: row.amount,
            description: row.description,
            ...(sel.categoryId && sel.categoryId !== UNCATEGORIZED
              ? { categoryId: sel.categoryId }
              : {}),
            ...(row.originalAmount !== undefined ? { originalAmount: row.originalAmount } : {}),
            ...(row.originalCurrency ? { originalCurrency: row.originalCurrency } : {}),
            // Per-row transfer flag. Always excludes this row from income/
            // expense aggregates. When `counterAccountId` is set, the backend
            // also creates the opposing leg on that account (ATM → Cash,
            // Salary → Brokerage). When `null`, the row imports as-is — the
            // case for MAIB → MAIB A2A where each PDF carries its own side.
            ...(sel.isTransfer ? { isTransfer: true, counterAccountId: sel.counterAccountId } : {}),
            // Received amount on the counter account, only when its currency
            // differs from the import currency.
            ...(crossCurrencyCounter ? { counterAmount: Number(sel.counterAmount) } : {}),
            // Optional per-row note. Omit the key entirely when blank (same
            // omit-when-empty rule as `categoryId`), and send the trimmed value
            // so leading/trailing whitespace never reaches the backend.
            ...(sel.notes.trim().length > 0 ? { notes: sel.notes.trim() } : {}),
          };
        })
        .filter((t): t is NonNullable<typeof t> => t !== null),
    };

    // Harvest confirmed learn-with-confirm rules. A rule ships only when its
    // row is included, learning is on, the keyword is non-blank, and a real
    // category is selected. Dedup by upper-cased keyword (last row wins) so a
    // statement that repeats a payee doesn't send conflicting rules.
    const learned = new Map<string, { keyword: string; categoryId: string }>();
    for (const sel of selections) {
      if (!sel.included || !sel.learnEnabled) continue;
      const keyword = sel.learnKeyword.trim();
      if (keyword.length === 0) continue;
      if (!sel.categoryId || sel.categoryId === UNCATEGORIZED) continue;
      learned.set(keyword.toUpperCase(), { keyword, categoryId: sel.categoryId });
    }
    if (learned.size > 0) {
      payload.learnedPatterns = Array.from(learned.values());
    }

    // Defensive: the commit button is disabled whenever the selected-row count
    // is 0, so this guard never fires through the UI.
    /* v8 ignore start */
    if (payload.transactions.length === 0) {
      toast.error('Select at least one transaction to import.');
      return;
    }
    /* v8 ignore stop */

    try {
      const result = await commit.mutateAsync(payload);
      const skipped = result.skippedDuplicates;
      toast.success(
        `Imported ${result.importedCount} transactions (${skipped} duplicates skipped)`,
      );
      router.push('/transactions');
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to import transactions.');
    }
  };

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="flex items-center gap-2">
            <Building2 className="h-4 w-4" aria-hidden />
            <span>{preview.bankSource}</span>
            <span className="text-xs font-normal text-muted-foreground">· {fileName}</span>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
            <Stat
              label="Period"
              value={`${formatShortDate(preview.statementPeriod.from)} → ${formatShortDate(
                preview.statementPeriod.to,
              )}`}
            />
            <Stat
              label="Opening"
              value={formatMoney(preview.summary.openingBalance, accountCurrency)}
            />
            <Stat
              label="Closing"
              value={formatMoney(preview.summary.closingBalance, accountCurrency)}
            />
            <Stat
              label="Net"
              value={
                <span className="space-x-2 tabular-nums">
                  <span className="text-emerald-500">
                    +{formatMoney(preview.summary.totalIn, accountCurrency)}
                  </span>
                  <span className="text-rose-500">
                    -{formatMoney(preview.summary.totalOut, accountCurrency)}
                  </span>
                </span>
              }
            />
            <Stat
              label="Fees"
              value={
                <span
                  data-testid="import-summary-fees"
                  className="text-rose-500"
                  title="commission, already part of Out"
                >
                  {formatMoney(preview.summary.totalFees, accountCurrency)}
                  <span className="ml-1 text-[10px] font-normal text-muted-foreground">
                    (in Out)
                  </span>
                </span>
              }
            />
          </div>
          {(() => {
            // maib books commissions INSIDE `totalOut`, and `closingBalance` is
            // maib's "Sold Disponibil" = the true balance = opening + in − out.
            // So the statement identity is opening + in − out (fees are NOT
            // subtracted again — they're already counted in Out). Surface it so
            // the preview visibly reconciles to the printed closing balance; a
            // small tolerance absorbs float noise from summing the parsed rows.
            const { openingBalance, totalIn, totalOut, closingBalance } = preview.summary;
            const reconciled = openingBalance + totalIn - totalOut;
            const matches = Math.abs(reconciled - closingBalance) < 0.005;
            return (
              <p
                data-testid="import-summary-reconciliation"
                className={`mt-4 text-xs tabular-nums ${
                  matches ? 'text-muted-foreground' : 'text-amber-600 dark:text-amber-500'
                }`}
              >
                Opening + In − Out = {formatMoney(reconciled, accountCurrency)}{' '}
                {matches ? (
                  <span>✓ matches closing</span>
                ) : (
                  <span>
                    ⚠ doesn&apos;t match closing ({formatMoney(closingBalance, accountCurrency)})
                  </span>
                )}
              </p>
            );
          })()}
        </CardContent>
      </Card>

      <div className="rounded-lg border bg-card p-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => dispatch({ type: 'include-all', preview })}
            >
              Include all
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => dispatch({ type: 'exclude-duplicates', preview })}
            >
              Exclude duplicates
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => dispatch({ type: 'reset', preview })}
            >
              Reset selections
            </Button>
          </div>
          <div className="text-sm text-muted-foreground" data-testid="import-counter">
            Importing {counters.count} of {preview.transactions.length} ·{' '}
            <span className="text-emerald-500">
              +{formatMoney(counters.income, accountCurrency)}
            </span>{' '}
            ·{' '}
            <span className="text-rose-500">-{formatMoney(counters.expense, accountCurrency)}</span>
          </div>
        </div>
        {/* Row-derived parse check: totals summed from every parsed row, matched
            against the PDF header so the user can trust the parse before commit. */}
        <p
          data-testid="import-rows-check"
          className="mt-2 border-t pt-2 text-xs tabular-nums text-muted-foreground"
        >
          From parsed rows: In {formatMoney(rowCheck.rowIn, accountCurrency)}{' '}
          {rowCheck.inMatches ? (
            <span>✓</span>
          ) : (
            <span className="text-amber-600 dark:text-amber-500">
              ⚠ (Δ {formatMoney(rowCheck.rowIn - preview.summary.totalIn, accountCurrency)})
            </span>
          )}{' '}
          · Out {formatMoney(rowCheck.rowOut, accountCurrency)}{' '}
          {rowCheck.outMatches ? (
            <span>✓</span>
          ) : (
            <span className="text-amber-600 dark:text-amber-500">
              ⚠ (Δ {formatMoney(rowCheck.rowOut - preview.summary.totalOut, accountCurrency)})
            </span>
          )}{' '}
          · Fees {formatMoney(rowCheck.rowFees, accountCurrency)}{' '}
          {rowCheck.feesMatches ? (
            <span>✓</span>
          ) : (
            <span className="text-amber-600 dark:text-amber-500">
              ⚠ (Δ {formatMoney(rowCheck.rowFees - preview.summary.totalFees, accountCurrency)})
            </span>
          )}
          <br />
          Opening + In − Out = {formatMoney(rowCheck.balance, accountCurrency)}
        </p>
      </div>

      <div className="rounded-lg border">
        <Table data-testid="import-preview-table">
          <TableHeader>
            <TableRow>
              <TableHead className="w-12">
                <span className="sr-only">Include</span>
              </TableHead>
              <TableHead className="w-32">Date</TableHead>
              <TableHead>Description</TableHead>
              <TableHead>Direction</TableHead>
              <TableHead className="text-right">Amount</TableHead>
              <TableHead className="w-56">Category</TableHead>
              <TableHead className="w-24 text-center">Transfer</TableHead>
              <TableHead className="w-56">Counter account</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {/*
              Each row is a memoized <ImportPreviewRow>. This is the core of the
              typing-lag fix: the reducer returns the SAME object reference for
              every row except the one being edited (`idx === action.index ? {…} : row`),
              so a memoized row whose other props are referentially stable will
              skip re-rendering when a *different* row changes. Without the memo,
              one keystroke in any free-text field re-rendered all rows — each of
              which mounts two Radix <Select>s — which is what froze a 900-row
              statement.

              All props below are stable across parent re-renders: `dispatch` is
              stable from useReducer; `row`/`sel` come from arrays whose unchanged
              entries keep identity; `categories`, `counterAccountOptions`,
              `currencyByAccountId`, `categoryNameById` are memoized; the callbacks
              are wrapped in useCallback. `isNoteOpen` is a primitive boolean that
              only changes for the toggled row.

              Keyed by array index, NOT by row content: a maib statement
              legitimately contains repeated rows with identical
              date+direction+amount+description (the parser keeps these snapshot
              duplicates on purpose), so a content-based key collides and makes
              React mis-associate per-row state. The list is parsed once and
              never reordered/inserted/removed, and every reducer action
              addresses rows by `idx`, so the index is a stable, correct key.
            */}
            {preview.transactions.map((row, idx) => (
              <ImportPreviewRow
                // biome-ignore lint/suspicious/noArrayIndexKey: the parser keeps duplicate statement rows (identical date/direction/amount/description), so a content key collides; the list is never reordered/inserted/removed and the reducer addresses rows by idx, making the index the correct stable key.
                key={idx}
                row={row}
                idx={idx}
                sel={selections[idx]}
                dispatch={dispatch}
                categories={categories}
                accountCurrency={accountCurrency}
                counterAccountOptions={counterAccountOptions}
                currencyByAccountId={currencyByAccountId}
                categoryNameById={categoryNameById}
                isNoteOpen={openNoteRows.has(idx)}
                onToggleNote={toggleNoteRow}
                onSelectCounterAccount={handleSelectCounterAccount}
                onRequestNewCategory={handleRequestNewCategory}
              />
            ))}
          </TableBody>
        </Table>
      </div>

      <div className="flex items-center justify-between">
        <Button asChild variant="ghost" disabled={commit.isPending}>
          <Link href="/transactions">Cancel</Link>
        </Button>
        <div className="flex items-center gap-2">
          <Button type="button" variant="outline" onClick={onCancel} disabled={commit.isPending}>
            Back
          </Button>
          <Button
            type="button"
            onClick={handleCommit}
            disabled={commit.isPending || counters.count === 0}
            data-testid="import-commit-button"
          >
            {commit.isPending ? 'Importing...' : `Import ${counters.count} transactions`}
          </Button>
        </div>
      </div>

      {/* Inline create-category dialog, shared with the settings manager. Opened
          from a row's category picker via the NEW_CATEGORY sentinel. Keyed on
          the triggering row index so the form (incl. its `defaultFlow`) remounts
          fresh each time a different row opens it. `defaultFlow` follows the
          row's direction so an Expense row mints an Expense category by default.
          On success we assign the new id to that exact row through the same
          `set-category` path the picker uses — preserving the learn-rule logic —
          then close the dialog. The new category shows up in `flowOptions` once
          `useCategories` refetches (the mutation already invalidates the query),
          and the row's `value` resolves to the assigned id at that point. */}
      {newCategoryRow !== null && (
        <CreateCategoryDialog
          key={`new-category-${newCategoryRow}`}
          open={newCategoryRow !== null}
          onOpenChange={(next) => {
            if (!next) setNewCategoryRow(null);
          }}
          defaultFlow={preview.transactions[newCategoryRow]?.direction ?? 'Expense'}
          onCreated={(category) => {
            const idx = newCategoryRow;
            const row = preview.transactions[idx];
            if (row) {
              dispatch({
                type: 'set-category',
                index: idx,
                categoryId: category.id,
                suggestedCategoryId: row.suggestedCategoryId ?? '',
                description: row.description,
              });
            }
            setNewCategoryRow(null);
          }}
        />
      )}
    </div>
  );
}

interface ImportPreviewRowProps {
  row: ParsedTransactionPreview;
  idx: number;
  // `sel` can be momentarily undefined if `selections` is shorter than
  // `transactions` (it never is in practice — both derive from the same preview —
  // but the original code defended against it, so we keep the optional type).
  sel: RowSelection | undefined;
  // Stable from useReducer — never changes identity, so it's safe to depend on
  // inside the row's own callbacks/effects without busting the memo.
  dispatch: Dispatch<SelectionsAction>;
  categories: CategoryDto[];
  accountCurrency: string;
  counterAccountOptions: AccountDto[];
  currencyByAccountId: Map<string, string>;
  categoryNameById: Map<string, string>;
  isNoteOpen: boolean;
  onToggleNote: (idx: number) => void;
  onSelectCounterAccount: (idx: number, counterAccountId: string | null) => void;
  onRequestNewCategory: (idx: number) => void;
}

/**
 * One preview table row, wrapped in React.memo (see `ImportPreviewRowComponent`
 * below — `ImportPreviewRow` is the memoized export). The whole point of the
 * extraction is that a memoized row only re-renders when ITS OWN props change.
 * Because the reducer preserves object identity for untouched rows and every
 * shared prop is referentially stable (memoized lookups + useCallback handlers),
 * editing one row leaves all other rows' props untouched and they skip rendering.
 *
 * Free-text inputs (Note, Amount received, learn-keyword) hold their value in
 * LOCAL state and commit to the reducer ON BLUR rather than on every keystroke.
 * This keeps per-character typing entirely inside this single row instance —
 * no global dispatch, no parent re-render, no neighbor re-render. The local
 * value is kept in sync with `sel` whenever the committed value changes from
 * outside (FX pre-fill, "Reset selections", inline category create), so external
 * resets still flow through. The Radix <Select>s (category, counter account)
 * keep dispatching immediately — those changes are infrequent and don't lag.
 */
function ImportPreviewRowComponent({
  row,
  idx,
  sel,
  dispatch,
  categories,
  accountCurrency,
  counterAccountOptions,
  currencyByAccountId,
  categoryNameById,
  isNoteOpen,
  onToggleNote,
  onSelectCounterAccount,
  onRequestNewCategory,
}: ImportPreviewRowProps) {
  const included = sel?.included ?? false;
  const rowIsTransfer = sel?.isTransfer ?? row.isTransfer;
  const isExpense = row.direction === 'Expense';

  // Per-direction category options. This used to be computed inline in the
  // parent's `.map`, allocating a brand-new filtered array for every row on
  // every parent render. Moving it here (derived from the stable `categories`
  // prop + this row's direction) means it only recomputes when categories load
  // or this row actually re-renders.
  const flowOptions = useMemo(
    () => categories.filter((c) => c.flow === row.direction || c.flow === 'Both'),
    [categories, row.direction],
  );

  // Committed (reducer) value for this row's note. The local editor mirrors it
  // but only writes back on blur (see below).
  const committedNote = sel?.notes ?? '';
  const committedCounterAmount = sel?.counterAmount ?? '';
  const committedLearnKeyword = sel?.learnKeyword ?? '';

  // ── Local-state-commit-on-blur for the three free-text fields ─────────────
  // Each input is controlled by LOCAL state while focused/typing so a keystroke
  // never dispatches to the shared reducer (which would rebuild `selections`
  // and re-render the parent). We commit the value to the reducer on blur.
  //
  // To honor external resets (FX pre-fill writes `counterAmount`, "Reset
  // selections" clears everything, inline category-create seeds a keyword), we
  // re-sync local state whenever the committed value changes. We track the last
  // committed value we synced from so we only overwrite local state on a genuine
  // *external* change — not on the echo of our own on-blur commit.
  const [noteDraft, setNoteDraft] = useState(committedNote);
  const [lastSyncedNote, setLastSyncedNote] = useState(committedNote);
  if (committedNote !== lastSyncedNote) {
    // Committed value changed from outside (reset, etc.) — adopt it. Done during
    // render via the "derive state from props" pattern so the input reflects the
    // new value immediately without an extra commit/paint.
    setNoteDraft(committedNote);
    setLastSyncedNote(committedNote);
  }

  const [counterAmountDraft, setCounterAmountDraft] = useState(committedCounterAmount);
  const [lastSyncedCounterAmount, setLastSyncedCounterAmount] = useState(committedCounterAmount);
  if (committedCounterAmount !== lastSyncedCounterAmount) {
    // Re-sync on FX pre-fill (handleSelectCounterAccount → set-counter-amount),
    // on counter-account change (reducer blanks it), and on Reset.
    setCounterAmountDraft(committedCounterAmount);
    setLastSyncedCounterAmount(committedCounterAmount);
  }

  const [learnKeywordDraft, setLearnKeywordDraft] = useState(committedLearnKeyword);
  const [lastSyncedLearnKeyword, setLastSyncedLearnKeyword] = useState(committedLearnKeyword);
  if (committedLearnKeyword !== lastSyncedLearnKeyword) {
    // Re-sync when the reducer re-seeds the keyword (set-category proposes one)
    // or on Reset.
    setLearnKeywordDraft(committedLearnKeyword);
    setLastSyncedLearnKeyword(committedLearnKeyword);
  }

  // Mute the amount color for transfer rows so the user can see at a glance
  // which rows the backend auto-flagged.
  const amountClass = rowIsTransfer
    ? 'text-right font-medium tabular-nums text-muted-foreground'
    : isExpense
      ? 'text-right font-medium tabular-nums text-rose-500'
      : 'text-right font-medium tabular-nums text-emerald-500';

  const noteText = noteDraft;
  const showNote = isNoteOpen || committedNote.trim().length > 0;

  return (
    <TableRow
      data-testid="import-preview-row"
      data-duplicate={row.isDuplicate ? 'true' : 'false'}
      data-transfer={rowIsTransfer ? 'true' : 'false'}
      className={row.isDuplicate ? 'bg-amber-500/10 hover:bg-amber-500/15' : undefined}
    >
      <TableCell>
        <Checkbox
          checked={included}
          data-testid={`import-row-checkbox-${idx}`}
          onChange={(e) =>
            dispatch({
              type: 'toggle',
              index: idx,
              included: e.target.checked,
            })
          }
          aria-label={`Include row ${idx + 1}`}
        />
      </TableCell>
      <TableCell className="text-muted-foreground">
        {formatShortDate(row.transactionDate)}
      </TableCell>
      <TableCell className="max-w-70">
        <div className="flex items-center gap-2">
          <div className="truncate" title={row.description}>
            {row.description}
          </div>
          {rowIsTransfer && (
            <Badge variant="outline" data-testid={`import-row-transfer-badge-${idx}`}>
              Transfer
            </Badge>
          )}
        </div>
        {row.isDuplicate && (
          <Badge
            variant="warning"
            className="mt-1"
            title="A matching transaction already exists for this date and amount"
          >
            Already imported
          </Badge>
        )}
        {row.originalAmount !== undefined && row.originalCurrency && (
          <div className="text-xs text-muted-foreground">
            {row.originalAmount} {row.originalCurrency}
          </div>
        )}
        {/* Reveal-on-demand note affordance. Notes annotate the memo, so the
            control lives in the Description cell rather than a dedicated
            (always-visible) column — the import table is already dense. The
            compact textarea shows when EITHER the user toggled this row open OR
            the row already holds a committed non-blank note, so a typed note
            stays visible/editable and a still-empty editor can be collapsed
            again. The textarea is controlled by LOCAL `noteDraft` and commits to
            the reducer on blur — typing never dispatches globally. */}
        {!showNote ? (
          <button
            type="button"
            onClick={() => onToggleNote(idx)}
            className="mt-1 inline-flex h-6 items-center gap-1 rounded px-1.5 text-[11px] text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            aria-label={`Add note for row ${idx + 1}`}
            data-testid={`import-row-note-toggle-${idx}`}
          >
            <StickyNote className="size-3" aria-hidden />
            <span>Add note</span>
          </button>
        ) : (
          <div className="mt-1.5">
            <textarea
              value={noteText}
              onChange={(e) =>
                // Cap at the backend's 500-char limit so an over-long paste can't
                // reach the wire. Local-only — no dispatch on keystroke.
                setNoteDraft(e.target.value.slice(0, 500))
              }
              onBlur={() => {
                // Commit to the reducer once, on blur. Skip the dispatch when
                // nothing changed so we don't needlessly rebuild `selections`.
                if (noteDraft !== committedNote) {
                  dispatch({ type: 'set-notes', index: idx, notes: noteDraft });
                  // Keep the sync baseline in step with what we just committed so
                  // the render-time re-sync above treats this as "no external
                  // change" and leaves the draft alone.
                  setLastSyncedNote(noteDraft);
                }
              }}
              rows={2}
              maxLength={500}
              placeholder="Note (optional)"
              className="flex w-full rounded-md border border-input bg-background px-2 py-1 text-xs ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
              aria-label={`Note for row ${idx + 1}`}
              data-testid={`import-row-note-input-${idx}`}
            />
          </div>
        )}
      </TableCell>
      <TableCell>
        {isExpense ? (
          <Badge variant="destructive">Expense</Badge>
        ) : (
          <Badge variant="success">Income</Badge>
        )}
      </TableCell>
      <TableCell className={amountClass}>
        {isExpense ? '-' : '+'}
        {formatMoney(row.amount, accountCurrency)}
      </TableCell>
      <TableCell>
        <Select
          value={sel?.categoryId ?? UNCATEGORIZED}
          onValueChange={(v) => {
            // The "+ New category…" item is an action, not a value: it must open
            // the create dialog and leave the row's selection untouched. Radix
            // fires this handler and THEN closes the popover, so simply opening
            // the dialog from here is safe — but we must early-return before the
            // `set-category` dispatch, otherwise the sentinel would become the
            // row's categoryId and the trigger would show a bogus label. The
            // parent remembers which row asked (via onRequestNewCategory) so
            // `onCreated` can assign the new id back to it.
            if (v === NEW_CATEGORY) {
              onRequestNewCategory(idx);
              return;
            }
            dispatch({
              type: 'set-category',
              index: idx,
              categoryId: v,
              suggestedCategoryId: row.suggestedCategoryId ?? '',
              description: row.description,
            });
          }}
        >
          <SelectTrigger className="h-9" data-testid={`import-row-category-${idx}`}>
            <SelectValue placeholder="Uncategorized" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={UNCATEGORIZED}>Uncategorized</SelectItem>
            {flowOptions.map((c) => (
              <SelectItem key={c.id} value={c.id}>
                {c.name}
              </SelectItem>
            ))}
            {/* Action item pinned to the bottom: opens the create dialog (see the
                onValueChange guard above). Never a real selectable value. */}
            <SelectItem value={NEW_CATEGORY} data-testid={`import-row-new-category-${idx}`}>
              + New category…
            </SelectItem>
          </SelectContent>
        </Select>
        {sel?.learnEnabled && (
          <div
            className="mt-2 flex items-center gap-1.5 rounded-md border border-dashed bg-muted/40 p-1.5"
            data-testid={`import-row-learn-${idx}`}
          >
            <div className="min-w-0 flex-1">
              {/* Learn-keyword input is also LOCAL-state-on-blur. The hint below
                  reads the live draft so the user sees the keyword they're typing
                  immediately, while the reducer is only touched on blur. */}
              <Input
                value={learnKeywordDraft}
                onChange={(e) => setLearnKeywordDraft(e.target.value)}
                onBlur={() => {
                  if (learnKeywordDraft !== committedLearnKeyword) {
                    dispatch({
                      type: 'set-learn-keyword',
                      index: idx,
                      keyword: learnKeywordDraft,
                    });
                    setLastSyncedLearnKeyword(learnKeywordDraft);
                  }
                }}
                className="h-7 text-xs"
                aria-label={`Keyword to remember for row ${idx + 1}`}
                data-testid={`import-row-learn-keyword-${idx}`}
              />
              <p className="mt-1 truncate text-[10px] leading-tight text-muted-foreground">
                Auto-categorize future ‘{learnKeywordDraft.trim() || '…'}’ as{' '}
                {categoryNameById.get(sel.categoryId) ?? 'this category'}
              </p>
            </div>
            <button
              type="button"
              onClick={() => dispatch({ type: 'dismiss-learn', index: idx })}
              className="grid size-6 shrink-0 place-items-center rounded text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              aria-label={`Don't remember a rule for row ${idx + 1}`}
              title="Don't remember this"
              data-testid={`import-row-learn-toggle-${idx}`}
            >
              <span aria-hidden>×</span>
            </button>
          </div>
        )}
      </TableCell>
      <TableCell className="text-center">
        <Checkbox
          checked={rowIsTransfer}
          data-testid={`import-row-transfer-${idx}`}
          onChange={(e) =>
            dispatch({
              type: 'toggle-transfer',
              index: idx,
              isTransfer: e.target.checked,
            })
          }
          aria-label={`Mark row ${idx + 1} as transfer`}
        />
      </TableCell>
      <TableCell>
        {rowIsTransfer ? (
          counterAccountOptions.length === 0 ? (
            // Empty: render disabled with a quiet hint. Counter is optional, so we
            // deliberately don't push the user toward creating accounts here.
            // Controlled with the same NONE_COUNTER value as the populated branch
            // so swapping between the two (when accounts hydrate) doesn't flip
            // Radix's hidden native <select> uncontrolled→controlled.
            <Select disabled value={NONE_COUNTER}>
              <SelectTrigger className="h-9" data-testid={`import-row-counter-${idx}`}>
                <SelectValue placeholder="No eligible accounts" />
              </SelectTrigger>
              <SelectContent />
            </Select>
          ) : (
            <>
              <Select
                // Bind the null state to the NONE_COUNTER sentinel so `value`
                // always matches a real `SelectItem` from the first render onward —
                // and matches the disabled (no-accounts) branch above. An empty
                // string / undefined matches no item, which makes Radix's hidden
                // native <select> flip uncontrolled→controlled and logs a warning
                // per transfer row when accounts hydrate. `onValueChange` maps the
                // sentinel back to null.
                value={sel?.counterAccountId ?? NONE_COUNTER}
                onValueChange={(v) => onSelectCounterAccount(idx, v === NONE_COUNTER ? null : v)}
              >
                <SelectTrigger className="h-9" data-testid={`import-row-counter-${idx}`}>
                  {/* Radix only shows `placeholder` when value matches no item, but
                      the controlled NONE_COUNTER value always matches the "(none)"
                      item. Drive the trigger text from state instead: "Optional"
                      while unpicked (preserves the affordance + the tested label),
                      otherwise the picked account name via SelectValue. */}
                  {sel?.counterAccountId ? (
                    <SelectValue />
                  ) : (
                    <span className="text-muted-foreground">Optional</span>
                  )}
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE_COUNTER}>(none)</SelectItem>
                  {counterAccountOptions.map((a) => (
                    <SelectItem key={a.id} value={a.id}>
                      {a.name} ({a.type})
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {(() => {
                // Cross-currency counter ⇒ collect the amount received in the
                // counter account's currency + show the live effective rate
                // (source amount / received amount). The rate line reads the live
                // local draft so it updates as the user types, while the reducer
                // commit happens on blur.
                const counterCcy = sel?.counterAccountId
                  ? currencyByAccountId.get(sel.counterAccountId)
                  : undefined;
                if (!counterCcy || counterCcy === accountCurrency) return null;
                const rateLabel = formatEffectiveRate(
                  row.amount,
                  Number(counterAmountDraft),
                  accountCurrency,
                  counterCcy,
                );
                return (
                  <div className="mt-2 space-y-1">
                    <label
                      htmlFor={`import-row-counter-amount-${idx}`}
                      className="block text-[10px] uppercase tracking-wide text-muted-foreground"
                    >
                      Amount received ({counterCcy})
                    </label>
                    <Input
                      id={`import-row-counter-amount-${idx}`}
                      data-testid={`import-row-counter-amount-${idx}`}
                      type="number"
                      step="0.01"
                      min="0"
                      value={counterAmountDraft}
                      onChange={(e) => setCounterAmountDraft(e.target.value)}
                      onBlur={() => {
                        // Commit on blur. A programmatic FX pre-fill writes the
                        // committed value directly (set-counter-amount), and the
                        // render-time re-sync above mirrors it into the draft — so
                        // the displayed value still updates on a pre-fill even
                        // though typing is local.
                        if (counterAmountDraft !== committedCounterAmount) {
                          dispatch({
                            type: 'set-counter-amount',
                            index: idx,
                            counterAmount: counterAmountDraft,
                          });
                          setLastSyncedCounterAmount(counterAmountDraft);
                        }
                      }}
                      className="h-8 text-xs"
                    />
                    {rateLabel && (
                      <p
                        className="text-[10px] text-muted-foreground"
                        data-testid={`import-row-rate-${idx}`}
                      >
                        {rateLabel}
                      </p>
                    )}
                  </div>
                );
              })()}
            </>
          )
        ) : null}
      </TableCell>
    </TableRow>
  );
}

// Memoized export. With the default shallow prop comparison, a row re-renders
// only when one of its own props changes identity/value. Combined with the
// reducer preserving object identity for untouched rows and the parent passing
// referentially stable shared props, editing one row (or typing into another
// row's local-state input) leaves this row's props untouched and it skips the
// render entirely — which is what makes a large preview feel instant again.
const ImportPreviewRow = memo(ImportPreviewRowComponent);

function Stat({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 text-sm font-medium tabular-nums">{value}</div>
    </div>
  );
}
