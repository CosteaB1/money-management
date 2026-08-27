'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
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
import { ApiError } from '@/src/lib/api/client';
import { useUpdateLoan } from '@/src/lib/api/loans';
import type { LoanDto } from '@/src/types/api';

const schema = z.object({
  counterparty: z
    .string()
    .trim()
    .min(1, 'Counterparty is required')
    .max(100, 'Counterparty must be 100 characters or less'),
  notes: z.string().trim().max(500, 'Notes must be 500 characters or less').optional(),
});

type FormValues = z.input<typeof schema>;
type ParsedValues = z.output<typeof schema>;

interface Props {
  loan: LoanDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Edits a loan's descriptive metadata — counterparty and notes only.
 * Principal, currency, direction, and loan date are fixed at creation
 * (PUT /loans/{id} doesn't accept them).
 */
export function EditLoanDialog({ loan, open, onOpenChange }: Props) {
  const [apiError, setApiError] = useState<string | null>(null);
  const { mutateAsync, isPending } = useUpdateLoan(loan.id);

  const {
    handleSubmit,
    register,
    reset,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      counterparty: loan.counterparty,
      notes: loan.notes ?? '',
    },
  });

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    setApiError(null);
    try {
      await mutateAsync({
        counterparty: parsed.counterparty,
        notes: parsed.notes && parsed.notes.length > 0 ? parsed.notes : null,
      });
      toast.success('Loan updated');
      onOpenChange(false);
    } catch (err) {
      if (err instanceof ApiError) {
        setApiError(err.message);
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to update loan');
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next);
        if (!next) {
          reset({ counterparty: loan.counterparty, notes: loan.notes ?? '' });
          setApiError(null);
        }
      }}
    >
      <DialogContent data-testid="edit-loan-dialog">
        <DialogHeader>
          <DialogTitle>Edit loan</DialogTitle>
          <DialogDescription>
            Update the loan&apos;s counterparty and notes. Principal, currency, and dates are fixed
            at creation.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="edit-loan-counterparty">Counterparty</Label>
            <Input
              id="edit-loan-counterparty"
              data-testid="edit-loan-counterparty-input"
              maxLength={100}
              {...register('counterparty')}
              aria-invalid={Boolean(errors.counterparty)}
              aria-describedby={errors.counterparty ? 'edit-loan-counterparty-error' : undefined}
            />
            {errors.counterparty && (
              <p
                id="edit-loan-counterparty-error"
                className="text-xs text-destructive"
                role="alert"
              >
                {errors.counterparty.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="edit-loan-notes">Notes (optional)</Label>
            <Textarea
              id="edit-loan-notes"
              data-testid="edit-loan-notes-input"
              maxLength={500}
              {...register('notes')}
              aria-invalid={Boolean(errors.notes)}
              aria-describedby={errors.notes ? 'edit-loan-notes-error' : undefined}
            />
            {errors.notes && (
              <p id="edit-loan-notes-error" className="text-xs text-destructive" role="alert">
                {errors.notes.message}
              </p>
            )}
          </div>

          {apiError && (
            <p className="text-sm text-destructive" role="alert" data-testid="edit-loan-error">
              {apiError}
            </p>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="edit-loan-submit-button">
              {isPending ? 'Saving...' : 'Save changes'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
