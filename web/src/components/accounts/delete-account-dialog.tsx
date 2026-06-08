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
import { useDeleteAccount } from '@/src/lib/api/accounts';

interface Props {
  account: { id: string; name: string };
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Fired after a successful delete (post-close) — e.g. to navigate away from a detail page. */
  onDeleted?: () => void;
}

/**
 * Confirms a permanent (hard) delete of an account. Unlike archiving this
 * CANNOT be undone, and the backend only permits it when the account has no
 * history — otherwise it 409s and we surface that message verbatim so the
 * user knows to archive instead.
 */
export function DeleteAccountDialog({ account, open, onOpenChange, onDeleted }: Props) {
  const deleteAccount = useDeleteAccount();
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleConfirm = async () => {
    setIsSubmitting(true);
    try {
      await deleteAccount.mutateAsync(account.id);
      toast.success(`Deleted "${account.name}"`);
      onOpenChange(false);
      onDeleted?.();
    } catch (err) {
      // The 409 detail is informative ("...has linked transactions, imports,
      // or goals and can't be permanently deleted. Archive it instead.") —
      // surface it verbatim rather than a generic fallback.
      toast.error(err instanceof Error ? err.message : 'Failed to delete account');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent data-testid="delete-account-dialog">
        <DialogHeader>
          <DialogTitle>Delete account permanently?</DialogTitle>
          <DialogDescription>
            This permanently removes <strong>{account.name}</strong> and cannot be undone. It only
            works if the account has no transactions, imports, or linked goals — otherwise archive
            it instead.
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
            data-testid="delete-account-confirm-button"
          >
            {isSubmitting ? 'Deleting...' : 'Delete permanently'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
