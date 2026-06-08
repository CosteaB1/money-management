'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { Plus } from 'lucide-react';
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
import { useAccounts } from '@/src/lib/api/accounts';
import { ApiError } from '@/src/lib/api/client';
import { useCreateGoal } from '@/src/lib/api/goals';
import { todayIsoUtc } from '@/src/lib/utils/date';

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

export function CreateGoalDialog() {
  const [open, setOpen] = useState(false);
  const accountsQuery = useAccounts(false);
  const { mutateAsync, isPending } = useCreateGoal();

  const accounts = accountsQuery.data?.filter((a) => !a.isArchived) ?? [];

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
      name: '',
      targetAmount: 0,
      targetDate: '',
      mode: 'manual',
      linkedAccountId: undefined,
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
      toast.success('Goal created');
      reset();
      setOpen(false);
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        setError('linkedAccountId', { message: 'Linked account not found.' });
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to create goal');
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
        <Button data-testid="add-goal-button">
          <Plus className="h-4 w-4" />
          Add goal
        </Button>
      </DialogTrigger>
      <DialogContent data-testid="create-goal-dialog">
        <DialogHeader>
          <DialogTitle>Add goal</DialogTitle>
          <DialogDescription>
            Track progress toward a savings target. Linked goals follow a chosen account&apos;s
            balance live; manual goals are updated by hand.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="goal-name">Name</Label>
            <Input
              id="goal-name"
              data-testid="goal-name-input"
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
            <Label htmlFor="goal-target-amount">Target amount (MDL)</Label>
            <Input
              id="goal-target-amount"
              data-testid="goal-target-amount-input"
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
            <Label htmlFor="goal-target-date">Target date (optional)</Label>
            <Input
              id="goal-target-date"
              data-testid="goal-target-date-input"
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

          <fieldset className="space-y-2" data-testid="goal-mode-fieldset">
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
                  data-testid="goal-mode-manual"
                />
                <span>Manual</span>
              </label>
              <label className="inline-flex items-center gap-2 text-sm">
                <input
                  type="radio"
                  value="linked"
                  checked={mode === 'linked'}
                  onChange={() => setValue('mode', 'linked', { shouldValidate: true })}
                  data-testid="goal-mode-linked"
                />
                <span>Linked to account</span>
              </label>
            </div>
            <p className="text-xs text-muted-foreground">
              {mode === 'linked'
                ? 'Saved progress mirrors the linked account balance in MDL.'
                : 'Saved progress is set by hand from the goal row menu.'}
            </p>
          </fieldset>

          {mode === 'linked' && (
            <div className="space-y-2">
              <Label htmlFor="goal-linked-account">Account</Label>
              <Select
                value={linkedAccountId}
                onValueChange={(v) => setValue('linkedAccountId', v, { shouldValidate: true })}
              >
                <SelectTrigger id="goal-linked-account" data-testid="goal-linked-account-select">
                  <SelectValue placeholder="Select account" />
                </SelectTrigger>
                <SelectContent>
                  {accounts.length === 0 ? (
                    <div className="px-2 py-1.5 text-sm text-muted-foreground">
                      No accounts available.
                    </div>
                  ) : (
                    accounts.map((a) => (
                      <SelectItem
                        key={a.id}
                        value={a.id}
                        data-testid={`goal-linked-account-option-${a.name}`}
                      >
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
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="goal-submit-button">
              {isPending ? 'Creating...' : 'Create goal'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
