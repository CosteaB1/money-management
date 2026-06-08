'use client';

import { Badge } from '@/src/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/src/components/ui/table';
import { cn } from '@/src/lib/utils/cn';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';
import type { GoalContributionDto, GoalDetailDto } from '@/src/types/api';

// Notes are truncated to keep the table dense — the full text is exposed
// via the native `title` attribute on hover so power users can still see
// it without an inline expanded view.
const NOTES_TRUNCATE = 60;

function amountClass(amount: number): string {
  if (amount > 0) return 'text-emerald-500';
  if (amount < 0) return 'text-rose-500';
  return 'text-muted-foreground';
}

function signedAmount(amount: number): string {
  // formatMoney already renders the minus sign for negatives — we only
  // need to prefix a "+" on positives. Zero stays bare.
  if (amount === 0) return formatMoney(0, 'MDL');
  if (amount > 0) return `+${formatMoney(amount, 'MDL')}`;
  return formatMoney(amount, 'MDL');
}

function truncate(value: string, max: number): string {
  if (value.length <= max) return value;
  return `${value.slice(0, max).trimEnd()}…`;
}

interface SourceBadgeProps {
  row: GoalContributionDto;
  linkedAccountName: string | null;
}

function SourceBadge({ row, linkedAccountName }: SourceBadgeProps) {
  if (row.source === 'Manual') {
    return (
      <Badge variant="outline" data-testid="goal-contribution-source-manual">
        Manual
      </Badge>
    );
  }
  return (
    <Badge
      variant="outline"
      className="text-muted-foreground"
      data-testid="goal-contribution-source-linked"
    >
      From {linkedAccountName ?? 'linked account'}
    </Badge>
  );
}

interface Props {
  goal: GoalDetailDto;
}

/**
 * Contributions history table. Read-only in v1 — the user manages
 * contributions either through the manual "Update saved" dialog (manual
 * goals) or by recording transactions on the linked account (linked
 * goals). Rows arrive descending by `occurredOn` from the backend.
 *
 * Linked-mode rows have `id === null` (no stable DB key — they're
 * projected on the fly from the underlying transactions), so we fall
 * back to the `occurredOn + index` composite for React keys.
 */
export function GoalContributionsTable({ goal }: Props) {
  const rows = goal.contributions;

  return (
    <div className="space-y-3" data-testid="goal-contributions-section">
      <h2 className="text-base font-semibold tracking-tight">Contributions</h2>
      <div className="rounded-lg border">
        <Table data-testid="goal-contributions-table">
          <TableHeader>
            <TableRow>
              <TableHead>Date</TableHead>
              <TableHead className="text-right">Amount</TableHead>
              <TableHead>Source</TableHead>
              <TableHead>Notes</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.length === 0 ? (
              <TableRow>
                <TableCell
                  colSpan={4}
                  className="py-10 text-center text-muted-foreground"
                  data-testid="goal-contributions-empty"
                >
                  No contributions logged yet.
                </TableCell>
              </TableRow>
            ) : (
              rows.map((row, index) => {
                const reactKey = row.id ?? `${row.occurredOn}-${index}`;
                const notes = row.notes ?? '';
                const truncated = truncate(notes, NOTES_TRUNCATE);
                return (
                  <TableRow key={reactKey} data-testid="goal-contribution-row">
                    <TableCell className="text-muted-foreground">
                      {formatShortDate(row.occurredOn)}
                    </TableCell>
                    <TableCell
                      className={cn('text-right tabular-nums font-medium', amountClass(row.amount))}
                      data-testid="goal-contribution-amount"
                    >
                      {signedAmount(row.amount)}
                    </TableCell>
                    <TableCell>
                      <SourceBadge row={row} linkedAccountName={goal.linkedAccountName} />
                    </TableCell>
                    <TableCell className="max-w-[24rem] text-muted-foreground">
                      {notes ? (
                        <span title={notes} data-testid="goal-contribution-notes">
                          {truncated}
                        </span>
                      ) : (
                        <span aria-hidden>—</span>
                      )}
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
