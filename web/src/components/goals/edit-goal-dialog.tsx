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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/src/components/ui/select';
import { useAccounts } from '@/src/lib/api/accounts';
import { ApiError } from '@/src/lib/api/client';
import { useUpdateGoal } from '@/src/lib/api/goals';
import { todayIsoUtc } from '@/src/lib/utils/date';
import type { GoalDto } from '@/src/types/api';

const today = () => todayIsoUtc();

const schema = z
  .object({
    name: z
      .string()
      .trim()
      .min(1, 'Name is required')
      .max(100, 'Name must be 100 characters or less'),
    targetAmount: z.coerce
      .number({ invalid_type_error: 'Target amount must be a number' })
      .positive('Target amount must be greater than 0'),
    targetDate: z
      .string()
      .optional()
      .refine((v) => !v || v >= today(), { message: 'Target date cannot be in the past' }),
    mode: z.enum(['linked', 'manual']),
    linkedAccountId: z.string().uuid('Pick an account').optional(),
  })
  .refine((d) => d.mode === 'manual' || !!d.linkedAccountId, {
    path: ['linkedAccountId'],
    message: 'Pick an account',
  });

type FormValues = z.input<typeof schema>;
type ParsedValues = z.output<typeof schema>;

interface Props {
  goal: GoalDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Edits a goal in place, including switching between linked and manual
 * mode. PUT /goals/{id} fully replaces the goal — `saved` is recomputed
 * server-side on the next read.
 */
export function EditGoalDialog({ goal, open, onOpenChange }: Props) {
  const accountsQuery = useAccounts(false);
  const accounts = accountsQuery.data?.filter((a) => !a.isArchived) ?? [];
  const { mutateAsync, isPending } = useUpdateGoal(goal.id);

  const initialMode: 'linked' | 'manual' = goal.isLinkedMode ? 'linked' : 'manual';

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
      name: goal.name,
      targetAmount: goal.targetAmount,
      targetDate: goal.targetDate ?? '',
      mode: initialMode,
      linkedAccountId: goal.linkedAccountId ?? undefined,
    },
  });

  const mode = watch('mode');
  const linkedAccountId = watch('linkedAccountId') ?? '';

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    try {
      await mutateAsync({
        name: parsed.name,
        targetAmount: parsed.targetAmount,
        ...(parsed.targetDate ? { targetDate: parsed.targetDate } : {}),
        ...(parsed.mode === 'linked' && parsed.linkedAccountId
          ? { linkedAccountId: parsed.linkedAccountId }
          : {}),
      });
      toast.success('Goal updated');
      onOpenChange(false);
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 404) {
          setError('linkedAccountId', { message: 'Linked account not found.' });
          return;
        }
      }
      toast.error(err instanceof Error ? err.message : 'Failed to update goal');
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next);
        if (!next) {
          reset({
            name: goal.name,
            targetAmount: goal.targetAmount,
            targetDate: goal.targetDate ?? '',
            mode: initialMode,
            linkedAccountId: goal.linkedAccountId ?? undefined,
          });
        }
      }}
    >
      <DialogContent data-testid="edit-goal-dialog">
        <DialogHeader>
          <DialogTitle>Edit goal</DialogTitle>
          <DialogDescription>
            Update <strong>{goal.name}</strong>. Switching between manual and linked mode recomputes
            the saved figure on the next refresh.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="edit-goal-name">Name</Label>
            <Input
              id="edit-goal-name"
              data-testid="edit-goal-name-input"
              maxLength={100}
              {...register('name')}
              aria-invalid={Boolean(errors.name)}
            />
            {errors.name && (
              <p className="text-xs text-destructive" role="alert">
                {errors.name.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="edit-goal-target-amount">Target amount (MDL)</Label>
            <Input
              id="edit-goal-target-amount"
              data-testid="edit-goal-target-amount-input"
              type="number"
              step="0.01"
              min="0"
              {...register('targetAmount')}
              aria-invalid={Boolean(errors.targetAmount)}
            />
            {errors.targetAmount && (
              <p className="text-xs text-destructive" role="alert">
                {errors.targetAmount.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="edit-goal-target-date">Target date (optional)</Label>
            <Input
              id="edit-goal-target-date"
              data-testid="edit-goal-target-date-input"
              type="date"
              min={today()}
              {...register('targetDate')}
              aria-invalid={Boolean(errors.targetDate)}
            />
            {errors.targetDate && (
              <p className="text-xs text-destructive" role="alert">
                {errors.targetDate.message}
              </p>
            )}
          </div>

          <fieldset className="space-y-2">
            <legend className="text-sm font-medium">Mode</legend>
            <div className="flex flex-col gap-2 rounded-md border bg-muted/20 p-3 sm:flex-row sm:gap-4">
              <label className="inline-flex items-center gap-2 text-sm">
                <input
                  type="radio"
                  value="manual"
                  checked={mode === 'manual'}
                  onChange={() => {
                    setValue('mode', 'manual', { shouldValidate: true });
                    setValue('linkedAccountId', undefined, { shouldValidate: false });
                  }}
                  data-testid="edit-goal-mode-manual"
                />
                <span>Manual</span>
              </label>
              <label className="inline-flex items-center gap-2 text-sm">
                <input
                  type="radio"
                  value="linked"
                  checked={mode === 'linked'}
                  onChange={() => setValue('mode', 'linked', { shouldValidate: true })}
                  data-testid="edit-goal-mode-linked"
                />
                <span>Linked to account</span>
              </label>
            </div>
          </fieldset>

          {mode === 'linked' && (
            <div className="space-y-2">
              <Label htmlFor="edit-goal-linked-account">Account</Label>
              <Select
                value={linkedAccountId}
                onValueChange={(v) => setValue('linkedAccountId', v, { shouldValidate: true })}
              >
                <SelectTrigger
                  id="edit-goal-linked-account"
                  data-testid="edit-goal-linked-account-select"
                >
                  <SelectValue placeholder="Select account" />
                </SelectTrigger>
                <SelectContent>
                  {accounts.length === 0 ? (
                    <div className="px-2 py-1.5 text-sm text-muted-foreground">
                      No accounts available.
                    </div>
                  ) : (
                    accounts.map((a) => (
                      <SelectItem key={a.id} value={a.id}>
                        {a.name}
                      </SelectItem>
                    ))
                  )}
                </SelectContent>
              </Select>
              {errors.linkedAccountId && (
                <p className="text-xs text-destructive" role="alert">
                  {errors.linkedAccountId.message}
                </p>
              )}
            </div>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="edit-goal-submit-button">
              {isPending ? 'Saving...' : 'Save changes'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
