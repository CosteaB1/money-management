'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { Button } from '@/src/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/src/components/ui/dialog';
import { Input } from '@/src/components/ui/input';
import { Label } from '@/src/components/ui/label';
import { Textarea } from '@/src/components/ui/textarea';
import { useUpdateAccount } from '@/src/lib/api/accounts';
import type { AccountDto } from '@/src/types/api';

const MAX_NAME = 100;
const MAX_NOTES = 1000;

const schema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'Name is required')
    .max(MAX_NAME, `Name must be ${MAX_NAME} characters or less`),
  notes: z
    .string()
    .trim()
    .max(MAX_NOTES, `Notes must be ${MAX_NOTES} characters or less`)
    .optional()
    .or(z.literal('')),
});

type FormValues = z.input<typeof schema>;
type ParsedValues = z.output<typeof schema>;

interface Props {
  account: AccountDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Edits an account's user-mutable metadata — `name` (required) and `notes`
 * (optional). Currency and type are fixed at creation, so they're shown as
 * read-only context only. Submits via `useUpdateAccount` (PUT /accounts/{id});
 * on success it toasts the new name, closes, and resets the form. Validation
 * (404 / 400) errors come back as an `ApiError` whose `.message` we surface
 * via a toast.
 */
export function EditAccountDialog({ account, open, onOpenChange }: Props) {
  const { mutateAsync, isPending } = useUpdateAccount(account.id);

  const {
    register,
    handleSubmit,
    reset,
    watch,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: account.name,
      notes: account.notes ?? '',
    },
  });

  const notesValue = watch('notes') ?? '';

  // Re-seed the form to the current account whenever it changes — the
  // controlled dialog instance is long-lived, so a fresh `account` prop
  // (e.g. after a cache refetch) must flow into the fields.
  const resetToAccount = () => {
    reset({ name: account.name, notes: account.notes ?? '' });
  };

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    try {
      const trimmedName = parsed.name.trim();
      await mutateAsync({
        name: trimmedName,
        notes: parsed.notes && parsed.notes.trim().length > 0 ? parsed.notes.trim() : null,
      });
      toast.success(`Renamed to "${trimmedName}"`);
      onOpenChange(false);
      resetToAccount();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to update account');
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next);
        if (!next) resetToAccount();
      }}
    >
      <DialogContent data-testid="edit-account-dialog">
        <DialogHeader>
          <DialogTitle>Edit account</DialogTitle>
          <DialogDescription>
            Rename <strong>{account.name}</strong> or update its notes. The currency (
            {account.currency}) and account type are fixed and can't be changed here.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="account-edit-name">Name</Label>
            <Input
              id="account-edit-name"
              data-testid="account-edit-name-input"
              maxLength={MAX_NAME}
              {...register('name')}
              aria-invalid={Boolean(errors.name)}
              aria-describedby={errors.name ? 'account-edit-name-error' : undefined}
            />
            {errors.name && (
              <p id="account-edit-name-error" className="text-xs text-destructive" role="alert">
                {errors.name.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="account-edit-notes">Notes (optional)</Label>
            <Textarea
              id="account-edit-notes"
              data-testid="account-edit-notes-input"
              maxLength={MAX_NOTES}
              rows={3}
              {...register('notes')}
              aria-invalid={Boolean(errors.notes)}
            />
            <div className="flex items-center justify-between">
              {errors.notes ? (
                <p className="text-xs text-destructive" role="alert">
                  {errors.notes.message}
                </p>
              ) : (
                <span />
              )}
              <span className="text-xs text-muted-foreground">
                {notesValue.length}/{MAX_NOTES}
              </span>
            </div>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="account-edit-submit-button">
              {isPending ? 'Saving...' : 'Save changes'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
