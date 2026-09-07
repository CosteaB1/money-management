'use client';

import { TriangleAlert } from 'lucide-react';
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
import { ApiError } from '@/src/lib/api/client';
import { useArchivePool } from '@/src/lib/api/pools';
import { formatMoney } from '@/src/lib/utils/currency';

/** The subset of a pool this dialog needs — served by both the list and detail DTOs. */
export interface ArchivablePool {
  id: string;
  name: string;
  currency: string;
  outsideCapital: number;
}

interface Props {
  pool: ArchivablePool;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Confirms archiving a pool.
 *
 * Two things the copy has to land:
 *
 * - **Archive is not delete.** It is the exit for a real pool that has run its
 *   course: everything it recorded survives and the pool stays drillable. A
 *   pool created by mistake is removed with `DeletePoolDialog` instead, and
 *   the two must not read as flavours of the same button. Archiving is
 *   reversible — an archived pool can be put back in service.
 *
 * - **It is refused while outside capital remains.** Letting it through would
 *   revert the owner fraction to 1.0 and quietly reabsorb the participants'
 *   money into net worth. When we can see that state coming, say so up front
 *   instead of letting the user meet a 409.
 */
export function ArchivePoolDialog({ pool, open, onOpenChange }: Props) {
  const archive = useArchivePool();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [apiError, setApiError] = useState<string | null>(null);

  const blocked = pool.outsideCapital > 0;

  const handleConfirm = async () => {
    setIsSubmitting(true);
    setApiError(null);
    try {
      await archive.mutateAsync(pool.id);
      toast.success(`Archived "${pool.name}"`);
      onOpenChange(false);
    } catch (err) {
      if (err instanceof ApiError) {
        setApiError(err.message);
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to archive pool');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next);
        if (!next) setApiError(null);
      }}
    >
      <DialogContent data-testid="archive-pool-dialog">
        <DialogHeader>
          <DialogTitle>Archive pool?</DialogTitle>
          <DialogDescription>
            Archiving hides <strong>{pool.name}</strong> from the list. Its ledger, participants and
            every transaction it wrote stay intact, and the pool remains viewable on its own page.
            The account goes back to counting in full towards your net worth.
            <span className="mt-3 block">
              You can unarchive it later if you change your mind. To get rid of a pool you created
              by mistake, delete it instead — archiving keeps everything it recorded.
            </span>
            {blocked && (
              <span
                data-testid="archive-pool-outside-warning"
                className="mt-3 flex items-start gap-2 text-amber-700 dark:text-amber-400"
              >
                <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
                <span>
                  Other people still hold{' '}
                  <strong>{formatMoney(pool.outsideCapital, pool.currency)}</strong> in this pool.
                  Archiving is refused while that is true — pay them out or record their exit first,
                  or their money would silently become yours.
                </span>
              </span>
            )}
          </DialogDescription>
        </DialogHeader>
        {apiError && (
          <p className="text-sm text-destructive" role="alert" data-testid="archive-pool-error">
            {apiError}
          </p>
        )}
        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button
            type="button"
            variant="destructive"
            disabled={isSubmitting || blocked}
            onClick={handleConfirm}
            data-testid="archive-pool-confirm-button"
          >
            {isSubmitting ? 'Archiving...' : 'Archive'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
