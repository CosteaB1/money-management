'use client';

import { MoreHorizontal } from 'lucide-react';
import { useState } from 'react';
import { ArchiveBudgetDialog } from '@/src/components/budgets/archive-budget-dialog';
import { EditBudgetDialog } from '@/src/components/budgets/edit-budget-dialog';
import { Badge } from '@/src/components/ui/badge';
import { Button } from '@/src/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/src/components/ui/dropdown-menu';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/src/components/ui/table';
import { useBudgets } from '@/src/lib/api/budgets';
import { cn } from '@/src/lib/utils/cn';
import { formatMoney } from '@/src/lib/utils/currency';
import type { BudgetDto, BudgetStatus } from '@/src/types/api';

// Visual cap so a 300%-over-budget row doesn't blow out the layout. The
// "Over" badge already communicates that the bar is saturated.
const PROGRESS_CAP = 1.2;

const STATUS_BAR: Record<BudgetStatus, string> = {
  OnTrack: 'bg-emerald-500',
  Warning: 'bg-amber-500',
  Over: 'bg-rose-500',
};

const STATUS_BADGE: Record<
  BudgetStatus,
  { variant: 'success' | 'warning' | 'destructive'; label: string }
> = {
  OnTrack: { variant: 'success', label: 'On track' },
  Warning: { variant: 'warning', label: 'Warning' },
  Over: { variant: 'destructive', label: 'Over' },
};

export function BudgetsTable() {
  const { data, isLoading, isError } = useBudgets();
  const [editTarget, setEditTarget] = useState<BudgetDto | null>(null);
  const [archiveTarget, setArchiveTarget] = useState<BudgetDto | null>(null);

  return (
    <div className="rounded-lg border">
      <Table data-testid="budgets-table">
        <TableHeader>
          <TableRow>
            <TableHead>Category</TableHead>
            <TableHead className="text-right">Monthly limit</TableHead>
            <TableHead className="text-right">Spent</TableHead>
            <TableHead className="text-right">Remaining</TableHead>
            <TableHead className="w-[240px]">Progress</TableHead>
            <TableHead>Status</TableHead>
            <TableHead className="w-12 text-right">
              <span className="sr-only">Actions</span>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isError ? (
            <TableRow>
              <TableCell colSpan={7} className="text-center text-destructive">
                Failed to load budgets.
              </TableCell>
            </TableRow>
          ) : isLoading || !data ? (
            <BudgetsSkeletonRows />
          ) : data.length === 0 ? (
            <TableRow>
              <TableCell colSpan={7} className="py-10 text-center text-muted-foreground">
                No budgets yet — click &ldquo;Add budget&rdquo; to create one.
              </TableCell>
            </TableRow>
          ) : (
            data.map((budget) => (
              <BudgetRow
                key={budget.id}
                budget={budget}
                onEdit={() => setEditTarget(budget)}
                onArchive={() => setArchiveTarget(budget)}
              />
            ))
          )}
        </TableBody>
      </Table>

      {editTarget && (
        <EditBudgetDialog
          budget={editTarget}
          open={editTarget !== null}
          onOpenChange={(next) => {
            if (!next) setEditTarget(null);
          }}
        />
      )}
      {archiveTarget && (
        <ArchiveBudgetDialog
          budget={archiveTarget}
          open={archiveTarget !== null}
          onOpenChange={(next) => {
            if (!next) setArchiveTarget(null);
          }}
        />
      )}
    </div>
  );
}

function BudgetRow({
  budget,
  onEdit,
  onArchive,
}: {
  budget: BudgetDto;
  onEdit: () => void;
  onArchive: () => void;
}) {
  const ratio = budget.monthlyLimit > 0 ? budget.spent / budget.monthlyLimit : 0;
  const cappedRatio = Math.min(Math.max(ratio, 0), PROGRESS_CAP);
  const widthPct = `${cappedRatio * 100}%`;
  const badge = STATUS_BADGE[budget.status];
  const remainingClass =
    budget.remaining < 0
      ? 'text-rose-500'
      : budget.remaining > 0
        ? 'text-emerald-500'
        : 'text-muted-foreground';

  return (
    <TableRow data-testid="budget-row" data-status={budget.status}>
      <TableCell className="font-medium">{budget.categoryName}</TableCell>
      <TableCell className="text-right tabular-nums">
        {formatMoney(budget.monthlyLimit, 'MDL')}
      </TableCell>
      <TableCell className="text-right tabular-nums text-muted-foreground">
        {formatMoney(budget.spent, 'MDL')}
      </TableCell>
      <TableCell className={cn('text-right font-medium tabular-nums', remainingClass)}>
        {formatMoney(budget.remaining, 'MDL')}
      </TableCell>
      <TableCell>
        <div
          className="h-1.5 w-full overflow-hidden rounded-full bg-muted"
          role="progressbar"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(Math.min(ratio, 1) * 100)}
          aria-label={`${budget.categoryName} spend ${Math.round(ratio * 100)} percent of limit`}
        >
          <div
            data-testid="budget-progress-bar"
            data-status={budget.status}
            className={cn('h-full transition-[width]', STATUS_BAR[budget.status])}
            style={{ width: widthPct }}
          />
        </div>
      </TableCell>
      <TableCell>
        <Badge variant={badge.variant} data-testid="budget-status-pill">
          {badge.label}
        </Badge>
      </TableCell>
      <TableCell className="text-right">
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              variant="ghost"
              size="icon"
              aria-label={`Actions for ${budget.categoryName} budget`}
              data-testid="budget-actions"
            >
              <MoreHorizontal className="h-4 w-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem onClick={onEdit} data-testid="edit-budget-action">
              Edit limit
            </DropdownMenuItem>
            <DropdownMenuItem onClick={onArchive} data-testid="archive-budget-action">
              Archive
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </TableCell>
    </TableRow>
  );
}

const SKELETON_ROW_IDS = ['s1', 's2', 's3'] as const;
const SKELETON_CELL_IDS = ['c1', 'c2', 'c3', 'c4', 'c5', 'c6', 'c7'] as const;

function BudgetsSkeletonRows() {
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
