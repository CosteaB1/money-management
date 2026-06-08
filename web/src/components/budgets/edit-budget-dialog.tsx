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
import { useUpdateBudgetLimit } from '@/src/lib/api/budgets';
import { ApiError } from '@/src/lib/api/client';
import { formatMoney } from '@/src/lib/utils/currency';
import type { BudgetDto } from '@/src/types/api';

const schema = z.object({
  monthlyLimit: z.coerce
    .number({ invalid_type_error: 'Monthly limit must be a number' })
    .positive('Monthly limit must be greater than 0'),
});

type FormValues = z.input<typeof schema>;
type ParsedValues = z.output<typeof schema>;

interface Props {
  budget: BudgetDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Edits the monthly cap on an existing budget. The category is fixed
 * because budgets are 1:1 with a category — a different category needs a
 * fresh budget (or archive-and-recreate).
 */
export function EditBudgetDialog({ budget, open, onOpenChange }: Props) {
  const { mutateAsync, isPending } = useUpdateBudgetLimit(budget.id);

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      monthlyLimit: budget.monthlyLimit,
    },
  });

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    try {
      await mutateAsync({ monthlyLimit: parsed.monthlyLimit });
      toast.success('Budget updated');
      reset({ monthlyLimit: parsed.monthlyLimit });
      onOpenChange(false);
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        setError('monthlyLimit', { message: 'Budget not found.' });
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to update budget');
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next);
        if (!next) reset({ monthlyLimit: budget.monthlyLimit });
      }}
    >
      <DialogContent data-testid="edit-budget-dialog">
        <DialogHeader>
          <DialogTitle>Edit budget</DialogTitle>
          <DialogDescription>
            Update the monthly cap for <strong>{budget.categoryName}</strong>. Current spend this
            month is{' '}
            <span className="font-medium tabular-nums">{formatMoney(budget.spent, 'MDL')}</span>.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label>Category</Label>
            <div
              className="rounded-md border border-dashed bg-muted/30 px-3 py-2 text-sm text-muted-foreground"
              data-testid="edit-budget-category-readonly"
            >
              {budget.categoryName}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="edit-budget-monthly-limit">Monthly limit (MDL)</Label>
            <Input
              id="edit-budget-monthly-limit"
              data-testid="edit-budget-monthly-limit-input"
              type="number"
              step="0.01"
              min="0"
              {...register('monthlyLimit')}
              aria-invalid={Boolean(errors.monthlyLimit)}
            />
            {errors.monthlyLimit && (
              <p className="text-xs text-destructive" role="alert">
                {errors.monthlyLimit.message}
              </p>
            )}
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="edit-budget-submit-button">
              {isPending ? 'Saving...' : 'Save changes'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
