'use client';

import { AlertTriangle, MoreHorizontal } from 'lucide-react';
import Link from 'next/link';
import { useState } from 'react';
import { ArchiveGoalDialog } from '@/src/components/goals/archive-goal-dialog';
import { EditGoalDialog } from '@/src/components/goals/edit-goal-dialog';
import { UpdateSavedDialog } from '@/src/components/goals/update-saved-dialog';
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
import { useGoals } from '@/src/lib/api/goals';
import { cn } from '@/src/lib/utils/cn';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatMonthYear } from '@/src/lib/utils/date';
import type { GoalDto, GoalStatus } from '@/src/types/api';

// Visual cap so a 300%-overshot goal doesn't blow out the layout.
const PROGRESS_CAP = 1.2;

const STATUS_BAR: Record<GoalStatus, string> = {
  OnTrack: 'bg-emerald-500',
  AtRisk: 'bg-amber-500',
  Achieved: 'bg-emerald-500',
  Behind: 'bg-rose-500',
};

const STATUS_BADGE: Record<
  GoalStatus,
  { variant: 'success' | 'warning' | 'destructive' | 'outline'; label: string }
> = {
  OnTrack: { variant: 'success', label: 'On track' },
  AtRisk: { variant: 'warning', label: 'At risk' },
  Achieved: { variant: 'outline', label: 'Achieved' },
  Behind: { variant: 'destructive', label: 'Behind' },
};

function formatTargetDate(value: string | null): string {
  if (!value) return '—';
  // value is "YYYY-MM-DD"; reuse the "MMM yyyy" helper (it takes "YYYY-MM").
  return formatMonthYear(value.slice(0, 7));
}

export function GoalsTable() {
  const { data, isLoading, isError } = useGoals();
  const [editTarget, setEditTarget] = useState<GoalDto | null>(null);
  const [archiveTarget, setArchiveTarget] = useState<GoalDto | null>(null);
  const [savedTarget, setSavedTarget] = useState<GoalDto | null>(null);

  return (
    <div className="rounded-lg border">
      <Table data-testid="goals-table">
        <TableHeader>
          <TableRow>
            <TableHead>Name</TableHead>
            <TableHead className="text-right">Target</TableHead>
            <TableHead className="text-right">Saved</TableHead>
            <TableHead className="w-[220px]">Progress</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Target date</TableHead>
            <TableHead>Mode</TableHead>
            <TableHead className="w-12 text-right">
              <span className="sr-only">Actions</span>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isError ? (
            <TableRow>
              <TableCell colSpan={8} className="text-center text-destructive">
                Failed to load goals.
              </TableCell>
            </TableRow>
          ) : isLoading || !data ? (
            <GoalsSkeletonRows />
          ) : data.length === 0 ? (
            <TableRow>
              <TableCell colSpan={8} className="py-10 text-center text-muted-foreground">
                No goals yet — click &ldquo;Add goal&rdquo; to create one.
              </TableCell>
            </TableRow>
          ) : (
            data.map((goal) => (
              <GoalRow
                key={goal.id}
                goal={goal}
                onEdit={() => setEditTarget(goal)}
                onUpdateSaved={() => setSavedTarget(goal)}
                onArchive={() => setArchiveTarget(goal)}
              />
            ))
          )}
        </TableBody>
      </Table>

      {editTarget && (
        <EditGoalDialog
          goal={editTarget}
          open={editTarget !== null}
          onOpenChange={(next) => {
            if (!next) setEditTarget(null);
          }}
        />
      )}
      {savedTarget && (
        <UpdateSavedDialog
          goal={savedTarget}
          open={savedTarget !== null}
          onOpenChange={(next) => {
            if (!next) setSavedTarget(null);
          }}
        />
      )}
      {archiveTarget && (
        <ArchiveGoalDialog
          goal={archiveTarget}
          open={archiveTarget !== null}
          onOpenChange={(next) => {
            if (!next) setArchiveTarget(null);
          }}
        />
      )}
    </div>
  );
}

function GoalRow({
  goal,
  onEdit,
  onUpdateSaved,
  onArchive,
}: {
  goal: GoalDto;
  onEdit: () => void;
  onUpdateSaved: () => void;
  onArchive: () => void;
}) {
  const ratio = goal.progressPercent;
  const cappedRatio = Math.min(Math.max(ratio, 0), PROGRESS_CAP);
  const widthPct = `${cappedRatio * 100}%`;
  const badge = STATUS_BADGE[goal.status];

  return (
    <TableRow
      data-testid="goal-row"
      data-status={goal.status}
      data-mode={goal.isLinkedMode ? 'linked' : 'manual'}
    >
      <TableCell className="font-medium">
        {/* Only the name cell is a link — keeps the row-action dropdown's
            pointer events isolated so clicking the menu trigger never
            navigates. Mirrors the accounts-table pattern. */}
        <Link
          href={`/goals/${goal.id}`}
          className="rounded-sm underline-offset-4 hover:text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
          data-testid="goal-name-link"
        >
          {goal.name}
        </Link>
      </TableCell>
      <TableCell className="text-right tabular-nums">
        {formatMoney(goal.targetAmount, 'MDL')}
      </TableCell>
      <TableCell className="text-right tabular-nums text-muted-foreground">
        <span className="inline-flex items-center justify-end gap-1.5">
          {formatMoney(goal.saved, 'MDL')}
          {goal.missingFxRate && (
            <span
              title="Couldn't FX-convert linked account"
              role="img"
              aria-label="Couldn't FX-convert linked account"
              data-testid="goal-missing-fx-icon"
              className="inline-flex"
            >
              <AlertTriangle className="h-3.5 w-3.5 text-amber-500" aria-hidden />
            </span>
          )}
        </span>
      </TableCell>
      <TableCell>
        <div
          className="h-1.5 w-full overflow-hidden rounded-full bg-muted"
          role="progressbar"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(Math.min(ratio, 1) * 100)}
          aria-label={`${goal.name} progress ${Math.round(ratio * 100)} percent of target`}
        >
          <div
            data-testid="goal-progress-bar"
            data-status={goal.status}
            className={cn(
              'h-full transition-[width]',
              STATUS_BAR[goal.status],
              goal.status === 'Achieved' && 'opacity-80 ring-1 ring-inset ring-emerald-400/60',
            )}
            style={{ width: widthPct }}
          />
        </div>
      </TableCell>
      <TableCell>
        <Badge variant={badge.variant} data-testid="goal-status-pill">
          {badge.label}
        </Badge>
      </TableCell>
      <TableCell className="text-muted-foreground" data-testid="goal-target-date">
        {formatTargetDate(goal.targetDate)}
      </TableCell>
      <TableCell>
        <div className="flex flex-col gap-0.5">
          <Badge
            variant={goal.isLinkedMode ? 'secondary' : 'outline'}
            data-testid="goal-mode-badge"
          >
            {goal.isLinkedMode ? 'Linked' : 'Manual'}
          </Badge>
          {goal.isLinkedMode && goal.linkedAccountName && (
            <span className="text-xs text-muted-foreground" data-testid="goal-linked-account-name">
              {goal.linkedAccountName}
            </span>
          )}
        </div>
      </TableCell>
      <TableCell className="text-right">
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              variant="ghost"
              size="icon"
              aria-label={`Actions for ${goal.name} goal`}
              data-testid="goal-actions"
            >
              <MoreHorizontal className="h-4 w-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem onClick={onEdit} data-testid="edit-goal-action">
              Edit
            </DropdownMenuItem>
            {!goal.isLinkedMode && (
              <DropdownMenuItem onClick={onUpdateSaved} data-testid="update-saved-action">
                Update saved
              </DropdownMenuItem>
            )}
            <DropdownMenuItem onClick={onArchive} data-testid="archive-goal-action">
              Archive
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </TableCell>
    </TableRow>
  );
}

const SKELETON_ROW_IDS = ['s1', 's2', 's3'] as const;
const SKELETON_CELL_IDS = ['c1', 'c2', 'c3', 'c4', 'c5', 'c6', 'c7', 'c8'] as const;

function GoalsSkeletonRows() {
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
