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
import { ApiError } from '@/src/lib/api/client';
import { useUpdateManualSaved } from '@/src/lib/api/goals';
import { formatMoney } from '@/src/lib/utils/currency';
import type { GoalDto } from '@/src/types/api';

const schema = z.object({
  amount: z.coerce
    .number({ invalid_type_error: 'Amount must be a number' })
    .min(0, 'Amount cannot be negative'),
});

type FormValues = z.input<typeof schema>;
type ParsedValues = z.output<typeof schema>;

interface Props {
  goal: GoalDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Sets the `saved` figure on a manual-mode goal. Only invokable from the
 * goal row when `isLinkedMode === false`. If the backend rejects with 400
 * (e.g. the goal was switched to linked mode in another tab), we close
 * the dialog and surface an error toast.
 */
export function UpdateSavedDialog({ goal, open, onOpenChange }: Props) {
  const { mutateAsync, isPending } = useUpdateManualSaved(goal.id);

  const {
    handleSubmit,
    register,
    reset,
    setError,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      amount: goal.saved,
    },
  });

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    try {
      await mutateAsync({ amount: parsed.amount });
      toast.success('Saved amount updated');
      onOpenChange(false);
    } catch (err) {
      if (err instanceof ApiError && err.status === 400) {
        const message = err.message || 'Goal is not in manual mode';
        setError('amount', { message });
        toast.error(message);
        onOpenChange(false);
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to update saved amount');
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next);
        if (!next) reset({ amount: goal.saved });
      }}
    >
      <DialogContent data-testid="update-saved-dialog">
        <DialogHeader>
          <DialogTitle>Update saved</DialogTitle>
          <DialogDescription>
            Set the saved figure for <strong>{goal.name}</strong>. Current saved:{' '}
            <span className="font-medium tabular-nums">{formatMoney(goal.saved, 'MDL')}</span> of{' '}
            {formatMoney(goal.targetAmount, 'MDL')}.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="update-saved-amount">Saved amount (MDL)</Label>
            <Input
              id="update-saved-amount"
              data-testid="update-saved-amount-input"
              type="number"
              step="0.01"
              min="0"
              {...register('amount')}
              aria-invalid={Boolean(errors.amount)}
            />
            {errors.amount && (
              <p className="text-xs text-destructive" role="alert">
                {errors.amount.message}
              </p>
            )}
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="update-saved-submit-button">
              {isPending ? 'Saving...' : 'Update saved'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
