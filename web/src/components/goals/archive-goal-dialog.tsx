'use client';

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
import { useArchiveGoal } from '@/src/lib/api/goals';
import type { GoalDto } from '@/src/types/api';

interface Props {
  goal: GoalDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Confirms archiving (soft-delete) of a goal. Archived goals drop off
 * the list and stop showing on the dashboard widget.
 */
export function ArchiveGoalDialog({ goal, open, onOpenChange }: Props) {
  const archive = useArchiveGoal();
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleConfirm = async () => {
    setIsSubmitting(true);
    try {
      await archive.mutateAsync(goal.id);
      toast.success(`Archived "${goal.name}" goal`);
      onOpenChange(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to archive goal');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent data-testid="archive-goal-dialog">
        <DialogHeader>
          <DialogTitle>Archive goal?</DialogTitle>
          <DialogDescription>
            Archiving stops tracking progress against <strong>{goal.name}</strong>. The goal
            disappears from the list — you can always create a fresh one later.
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
            data-testid="archive-goal-confirm-button"
          >
            {isSubmitting ? 'Archiving...' : 'Archive'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
