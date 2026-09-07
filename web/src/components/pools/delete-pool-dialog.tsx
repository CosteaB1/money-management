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
import { useDeletePool } from '@/src/lib/api/pools';
import { formatMoney } from '@/src/lib/utils/currency';

/** The subset of a pool this dialog needs — served by both the list and detail DTOs. */
export interface DeletablePool {
  id: string;
  name: string;
  accountName: string;
  currency: string;
  outsideCapital: number;
  isArchived: boolean;
}

interface Props {
  pool: DeletablePool;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Fired after a successful delete (post-close) — e.g. to leave a now-dead detail page. */
  onDeleted?: () => void;
  /**
   * Offered as the next step when the backend refuses the delete. Omit on
   * surfaces that have nowhere to open the archive confirm.
   */
  onArchiveInstead?: () => void;
}

/** The one 409 this route can answer with — see `PoolErrors.DeleteHasMovements`. */
const HAS_MOVEMENTS = 'pools.delete_has_movements';

/** ProblemDetails carries the machine code as `errorCode`; a few mocks use `code`. */
function errorCodeOf(err: ApiError): string | null {
  const body =
    err.body && typeof err.body === 'object' ? (err.body as Record<string, unknown>) : null;
  const code = body?.errorCode ?? body?.code;
  return typeof code === 'string' ? code : null;
}

/**
 * Confirms a permanent delete of a pool.
 *
 * **Delete is not a stronger Archive, and the copy has to keep the two apart.**
 * Archive is for a real pool that has run its course: the ledger, the roster
 * and every transaction it wrote stay, and the pool is still drillable. Delete
 * is for a pool that never moved anyone's money — the wrong account, a
 * mis-click — and it removes the pool, its participants and its ledger outright.
 *
 * The sentence that carries the most weight is the one about transactions:
 * a **204 does not roll the account's history back**. A pool's catch-up mark is
 * a real adjustment row that moved the balance to what the exchange was
 * actually showing, and may already have been reconciled against, so deleting
 * the pool deliberately leaves it alone. Anyone who assumes their balance
 * reverts will misread their own account, which is why it is stated outright
 * rather than left to be inferred from "the pool is removed".
 *
 * `pools.delete_has_movements` is the expected answer for any pool that has
 * actually been used, so it is rendered inline as guidance — money moved here,
 * archive it instead — with the dialog left open and Archive offered as the
 * next step whenever archiving would actually be permitted.
 */
export function DeletePoolDialog({ pool, open, onOpenChange, onDeleted, onArchiveInstead }: Props) {
  const deletePool = useDeletePool();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [apiError, setApiError] = useState<string | null>(null);
  const [blocked, setBlocked] = useState(false);

  // Archiving is refused while other people hold a stake, so recommending it
  // in that state would just walk the user into a second refusal.
  const outsideCapitalRemains = pool.outsideCapital > 0;
  const canArchiveInstead =
    onArchiveInstead !== undefined && !pool.isArchived && !outsideCapitalRemains;

  const reset = () => {
    setApiError(null);
    setBlocked(false);
  };

  const handleConfirm = async () => {
    setIsSubmitting(true);
    reset();
    try {
      await deletePool.mutateAsync(pool.id);
      toast.success(`Deleted "${pool.name}"`);
      onOpenChange(false);
      onDeleted?.();
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 409 && errorCodeOf(err) === HAS_MOVEMENTS) {
          // Expected outcome, not a failure — keep the dialog open and explain
          // the route out instead of flashing a toast the user has to catch.
          setBlocked(true);
          return;
        }
        // Anything else (404 for an id that is already gone, a validation
        // refusal) is surfaced verbatim rather than rewritten.
        setApiError(err.message);
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to delete pool');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next);
        if (!next) reset();
      }}
    >
      <DialogContent data-testid="delete-pool-dialog">
        <DialogHeader>
          <DialogTitle>Delete pool?</DialogTitle>
          <DialogDescription>
            Delete is for a pool created by mistake — one that never moved anyone&rsquo;s money. It
            permanently removes <strong>{pool.name}</strong>, everyone listed in it, and its whole
            ledger. This cannot be undone.
            {/* The load-bearing paragraph. Lives inside DialogDescription so
                aria-describedby announces it with the dialog; span-based
                because Radix renders the description as a <p>. */}
            <span className="mt-3 block" data-testid="delete-pool-transactions-note">
              <strong>Transactions on {pool.accountName} are not touched.</strong> Nothing is
              reversed — any balance adjustment this pool wrote stays on the account, because it
              genuinely moved the balance and you may already have reconciled against it. If one of
              those is wrong, delete it yourself from the transactions page.
            </span>
            {!pool.isArchived && (
              <span className="mt-3 block text-muted-foreground">
                If this pool was real and has simply run its course, archive it instead — that keeps
                the ledger, the roster and the history, and the pool stays drillable.
              </span>
            )}
          </DialogDescription>
        </DialogHeader>
        {blocked && (
          <div
            className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-700 dark:text-amber-400"
            role="alert"
            data-testid="delete-pool-blocked"
          >
            <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
            <span>
              Money has moved through this pool — it holds shares for someone other than you, or has
              entries that moved cash — so it can&rsquo;t be deleted. Deleting it would erase the
              record of real money. Archive it instead: the history stays and the pool stays
              viewable.
              {outsideCapitalRemains && (
                <>
                  {' '}
                  Other people still hold{' '}
                  <strong>{formatMoney(pool.outsideCapital, pool.currency)}</strong> here, so
                  archiving is refused too — pay them out or record their exit first.
                </>
              )}
              {pool.isArchived && (
                <> This pool is already archived, so there is nothing left to do.</>
              )}
            </span>
          </div>
        )}
        {apiError && (
          <p className="text-sm text-destructive" role="alert" data-testid="delete-pool-error">
            {apiError}
          </p>
        )}
        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          {blocked && canArchiveInstead && (
            <Button
              type="button"
              variant="secondary"
              onClick={() => {
                onOpenChange(false);
                onArchiveInstead?.();
              }}
              data-testid="delete-pool-archive-instead"
            >
              Archive instead
            </Button>
          )}
          <Button
            type="button"
            variant="destructive"
            disabled={isSubmitting || blocked}
            onClick={handleConfirm}
            data-testid="delete-pool-confirm-button"
          >
            {isSubmitting ? 'Deleting...' : 'Delete pool'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
