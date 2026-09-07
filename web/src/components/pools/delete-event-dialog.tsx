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
import { useDeletePoolEvent } from '@/src/lib/api/pools';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';
import { POOL_EVENT_LABEL } from '@/src/lib/utils/pool';
import type { PoolUnitEventDto } from '@/src/types/api';

interface Props {
  poolId: string;
  poolCurrency: string;
  event: PoolUnitEventDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Destructive confirm for removing a ledger row.
 *
 * Deleting an entry re-prices history: every share issued after it was struck
 * against a pool value that included this one, so removing it silently shifts
 * value between the owner and the participants. That is why the copy is blunt
 * rather than reassuring — the app cannot put it back, and the reconciliation
 * panel will start reporting drift if the account still carries the matching
 * transaction.
 *
 * Two backend behaviours the user has to know before pressing the button:
 * a linked transaction is removed alongside the entry, and deleting half of a
 * shared cost is impossible — the whole same-day pair goes, because removing
 * one side would change the total share count and the price with it.
 */
export function DeleteEventDialog({ poolId, poolCurrency, event, open, onOpenChange }: Props) {
  const deleteEvent = useDeletePoolEvent(poolId);
  const [apiError, setApiError] = useState<string | null>(null);

  const isCostTransfer = event.kind === 'CostShare' || event.kind === 'CostRecovery';

  const handleConfirm = async () => {
    setApiError(null);
    try {
      await deleteEvent.mutateAsync(event.id);
      toast.success('Ledger entry deleted');
      onOpenChange(false);
    } catch (err) {
      if (err instanceof ApiError) {
        setApiError(err.message);
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to delete the entry');
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
      <DialogContent data-testid="delete-pool-event-dialog">
        <DialogHeader>
          <DialogTitle>Delete this ledger entry?</DialogTitle>
          <DialogDescription>
            {POOL_EVENT_LABEL[event.kind]} for <strong>{event.participantName}</strong> on{' '}
            {formatShortDate(event.occurredOn)}
            {event.cash !== null
              ? `, ${formatMoney(event.cash, event.cashCurrency ?? poolCurrency)}`
              : ''}
            .
            <span
              data-testid="delete-pool-event-warning"
              className="mt-3 flex items-start gap-2 text-amber-700 dark:text-amber-400"
            >
              <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
              <span>
                Every share issued after this entry was priced against a pool value that included
                it. Removing it re-prices history and shifts value between you and the other
                participants — the app will report the mismatch, but it will not fix it.
                {event.movementTransactionId !== null && (
                  <>
                    {' '}
                    The transaction it wrote on{' '}
                    <strong>{event.movementAccountName ?? 'the linked account'}</strong> is deleted
                    too.
                  </>
                )}
                {isCostTransfer && (
                  <>
                    {' '}
                    Both halves of the shared cost go — half of one would change the share count.
                  </>
                )}
              </span>
            </span>
          </DialogDescription>
        </DialogHeader>
        {apiError && (
          <p
            className="text-sm text-destructive"
            role="alert"
            data-testid="delete-pool-event-error"
          >
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
            disabled={deleteEvent.isPending}
            onClick={handleConfirm}
            data-testid="delete-pool-event-confirm"
          >
            {deleteEvent.isPending ? 'Deleting...' : 'Delete entry'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
