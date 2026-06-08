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
import { useArchiveBudget } from '@/src/lib/api/budgets';
import type { BudgetDto } from '@/src/types/api';

interface Props {
  budget: BudgetDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Confirms archiving (soft-delete) of a budget. Archived budgets stop
 * tracking spend and disappear from the list — the underlying category
 * is untouched, so the user can re-add a budget for it later.
 */
export function ArchiveBudgetDialog({ budget, open, onOpenChange }: Props) {
  const archive = useArchiveBudget();
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleConfirm = async () => {
    setIsSubmitting(true);
    try {
      await archive.mutateAsync(budget.id);
      toast.success(`Archived "${budget.categoryName}" budget`);
      onOpenChange(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to archive budget');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent data-testid="archive-budget-dialog">
        <DialogHeader>
          <DialogTitle>Archive budget?</DialogTitle>
          <DialogDescription>
            Archiving stops tracking spend against <strong>{budget.categoryName}</strong>. The
            category itself stays — you can add a fresh budget for it later if you change your mind.
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
            data-testid="archive-budget-confirm-button"
          >
            {isSubmitting ? 'Archiving...' : 'Archive'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
