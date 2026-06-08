'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
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
  DialogTrigger,
} from '@/src/components/ui/dialog';
import { Input } from '@/src/components/ui/input';
import { Label } from '@/src/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/src/components/ui/select';
import { useBudgets, useCreateBudget } from '@/src/lib/api/budgets';
import { useCategories } from '@/src/lib/api/categories';
import { ApiError } from '@/src/lib/api/client';

const schema = z.object({
  categoryId: z.string().uuid('Select a category'),
  monthlyLimit: z.coerce
    .number({ invalid_type_error: 'Monthly limit must be a number' })
    .positive('Monthly limit must be greater than 0'),
});

type FormValues = z.input<typeof schema>;
type ParsedValues = z.output<typeof schema>;

export function CreateBudgetDialog() {
  const [open, setOpen] = useState(false);
  const categoriesQuery = useCategories({ includeArchived: false });
  // Used to grey out categories that already have a live budget for the
  // current month — backend still rejects with 409, but pre-disabling
  // them in the Select reduces the chance the user even tries.
  const existingBudgetsQuery = useBudgets();
  const { mutateAsync, isPending } = useCreateBudget();

  // Budgets only make sense for expense-flow categories. "Both"-flow
  // categories (e.g. "Misc") qualify too because the spend side counts.
  const budgetableCategories = useMemo(
    () => categoriesQuery.data?.filter((c) => c.flow !== 'Income' && !c.isArchived) ?? [],
    [categoriesQuery.data],
  );

  const existingCategoryIds = useMemo(
    () => new Set(existingBudgetsQuery.data?.map((b) => b.categoryId) ?? []),
    [existingBudgetsQuery.data],
  );

  const {
    handleSubmit,
    register,
    reset,
    setValue,
    setError,
    watch,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      categoryId: '',
      monthlyLimit: 0,
    },
  });

  const categoryId = watch('categoryId') ?? '';

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    try {
      await mutateAsync({
        categoryId: parsed.categoryId,
        monthlyLimit: parsed.monthlyLimit,
      });
      toast.success('Budget created');
      reset();
      setOpen(false);
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 409) {
          setError('categoryId', {
            message: 'A budget already exists for this category.',
          });
          return;
        }
        if (err.status === 404) {
          setError('categoryId', { message: 'Category not found.' });
          return;
        }
      }
      toast.error(err instanceof Error ? err.message : 'Failed to create budget');
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) reset();
      }}
    >
      <DialogTrigger asChild>
        <Button data-testid="add-budget-button">
          <Plus className="h-4 w-4" />
          Add budget
        </Button>
      </DialogTrigger>
      <DialogContent data-testid="create-budget-dialog">
        <DialogHeader>
          <DialogTitle>Add budget</DialogTitle>
          <DialogDescription>
            Sets a monthly spending ceiling for one category. The dashboard tracks{' '}
            <span className="font-medium">spent / limit</span> live as expenses post.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="budget-category">Category</Label>
            <Select
              value={categoryId}
              onValueChange={(v) => setValue('categoryId', v, { shouldValidate: true })}
            >
              <SelectTrigger id="budget-category" data-testid="budget-category-select">
                <SelectValue placeholder="Select category" />
              </SelectTrigger>
              <SelectContent>
                {budgetableCategories.length === 0 ? (
                  <div className="px-2 py-1.5 text-sm text-muted-foreground">
                    No expense categories available.
                  </div>
                ) : (
                  budgetableCategories.map((c) => {
                    const alreadyBudgeted = existingCategoryIds.has(c.id);
                    return (
                      <SelectItem
                        key={c.id}
                        value={c.id}
                        disabled={alreadyBudgeted}
                        data-testid={`budget-category-option-${c.name}`}
                      >
                        {c.name}
                        {alreadyBudgeted ? ' (already budgeted)' : ''}
                      </SelectItem>
                    );
                  })
                )}
              </SelectContent>
            </Select>
            {errors.categoryId && (
              <p className="text-xs text-destructive" role="alert">
                {errors.categoryId.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="budget-monthly-limit">Monthly limit (MDL)</Label>
            <Input
              id="budget-monthly-limit"
              data-testid="budget-monthly-limit-input"
              type="number"
              step="0.01"
              min="0"
              {...register('monthlyLimit')}
              aria-invalid={Boolean(errors.monthlyLimit)}
              aria-describedby={errors.monthlyLimit ? 'budget-monthly-limit-error' : undefined}
            />
            {errors.monthlyLimit && (
              <p id="budget-monthly-limit-error" className="text-xs text-destructive" role="alert">
                {errors.monthlyLimit.message}
              </p>
            )}
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="budget-submit-button">
              {isPending ? 'Creating...' : 'Create budget'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
