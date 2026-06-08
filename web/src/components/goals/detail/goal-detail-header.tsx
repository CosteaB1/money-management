'use client';

import { ArrowLeft, Pencil, Trash2, Wallet } from 'lucide-react';
import Link from 'next/link';
import { useState } from 'react';
import { ArchiveGoalDialog } from '@/src/components/goals/archive-goal-dialog';
import { EditGoalDialog } from '@/src/components/goals/edit-goal-dialog';
import { UpdateSavedDialog } from '@/src/components/goals/update-saved-dialog';
import { Badge } from '@/src/components/ui/badge';
import { Button } from '@/src/components/ui/button';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';
import type { GoalDetailDto, GoalDto } from '@/src/types/api';

interface Props {
  goal: GoalDetailDto;
}

/**
 * Top strip of the detail page: back link, name, target badge, target-date
 * subtitle, mode badge, archived badge, and the action button group.
 *
 * The action buttons reuse the same dialogs the list page uses
 * (`EditGoalDialog`, `UpdateSavedDialog`, `ArchiveGoalDialog`). Each dialog
 * already accepts the optional controlled-open props — we drive them from
 * local state instead of clicking through a row menu.
 */
export function GoalDetailHeader({ goal }: Props) {
  // Shape the detail DTO into the lighter GoalDto that the three child
  // dialogs expect — they were designed around the list endpoint's shape,
  // and we don't want to leak the detail-only fields (pace, savedHistory,
  // contributions, createdOn) into their props.
  const goalForDialogs: GoalDto = {
    id: goal.id,
    name: goal.name,
    targetAmount: goal.targetAmount,
    targetDate: goal.targetDate,
    linkedAccountId: goal.linkedAccountId,
    linkedAccountName: goal.linkedAccountName,
    saved: goal.saved,
    remaining: goal.remaining,
    progressPercent: goal.progressPercent,
    status: goal.status,
    requiredMonthlyContribution: goal.requiredMonthlyContribution,
    isLinkedMode: goal.isLinkedMode,
    missingFxRate: goal.missingFxRate,
  };

  const [editOpen, setEditOpen] = useState(false);
  const [savedOpen, setSavedOpen] = useState(false);
  const [archiveOpen, setArchiveOpen] = useState(false);

  return (
    <div className="space-y-4" data-testid="goal-detail-header">
      <div>
        <Link
          href="/goals"
          aria-label="Back to goals"
          className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
          data-testid="goal-detail-back"
        >
          <ArrowLeft className="h-4 w-4" />
          Goals
        </Link>
      </div>

      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-semibold tracking-tight" data-testid="goal-detail-name">
              {goal.name}
            </h1>
            <Badge variant="secondary" data-testid="goal-detail-target">
              {formatMoney(goal.targetAmount, 'MDL')}
            </Badge>
            <Badge
              variant={goal.isLinkedMode ? 'secondary' : 'outline'}
              data-testid="goal-detail-mode"
            >
              {goal.isLinkedMode ? `Linked: ${goal.linkedAccountName ?? 'account'}` : 'Manual'}
            </Badge>
            {goal.isArchived && (
              <Badge variant="outline" data-testid="goal-detail-archived">
                Archived
              </Badge>
            )}
          </div>
          <p className="text-sm text-muted-foreground" data-testid="goal-detail-target-date">
            Target date{' '}
            {goal.targetDate ? (
              formatShortDate(goal.targetDate)
            ) : (
              <span className="text-muted-foreground" role="img" aria-label="No target date">
                —
              </span>
            )}
          </p>
        </div>

        {!goal.isArchived && (
          <div className="flex flex-wrap items-center gap-2" data-testid="goal-detail-actions">
            <Button
              variant="outline"
              size="sm"
              onClick={() => setEditOpen(true)}
              data-testid="goal-detail-edit"
            >
              <Pencil className="h-4 w-4" />
              Edit goal
            </Button>
            {/* Update saved is gated to manual-mode goals — the linked-mode
                saved figure is sourced from the linked account, so the
                backend rejects PATCH /manual-saved with 400. */}
            {!goal.isLinkedMode && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => setSavedOpen(true)}
                data-testid="goal-detail-update-saved"
              >
                <Wallet className="h-4 w-4" />
                Update saved
              </Button>
            )}
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setArchiveOpen(true)}
              data-testid="goal-detail-archive"
            >
              <Trash2 className="h-4 w-4" />
              Archive
            </Button>
          </div>
        )}
      </div>

      {/* Controlled instances of the existing list-page dialogs. Each accepts
          the optional `open`/`onOpenChange` props we extended them with in
          the goals-table refactor, so the trigger element is hidden in
          this externally-controlled mode. */}
      <EditGoalDialog goal={goalForDialogs} open={editOpen} onOpenChange={setEditOpen} />
      {!goal.isLinkedMode && (
        <UpdateSavedDialog goal={goalForDialogs} open={savedOpen} onOpenChange={setSavedOpen} />
      )}
      <ArchiveGoalDialog goal={goalForDialogs} open={archiveOpen} onOpenChange={setArchiveOpen} />
    </div>
  );
}
