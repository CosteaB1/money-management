'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { PoolValueNowField } from '@/src/components/pools/pool-value-now-field';
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
import { Textarea } from '@/src/components/ui/textarea';
import { ApiError } from '@/src/lib/api/client';
import { useRecordSubscription } from '@/src/lib/api/pools';
import { formatMoney } from '@/src/lib/utils/currency';
import type { PoolDetailDto } from '@/src/types/api';

const schema = z.object({
  participantId: z.string().uuid('Pick who the money is from'),
  poolValueNow: z.coerce
    .number({ invalid_type_error: 'The exchange total must be a number' })
    .positive('Type the exchange total before recording money in'),
  cash: z.coerce
    .number({ invalid_type_error: 'Amount must be a number' })
    .positive('Amount must be greater than 0'),
  notes: z.string().trim().max(500, 'Notes must be 500 characters or less').optional(),
});

type FormValues = z.input<typeof schema>;
type ParsedValues = z.output<typeof schema>;

interface Props {
  pool: PoolDetailDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Money in. Mints shares at the price that applies the instant the cash lands,
 * which leaves everyone else's per-share value untouched — that invariance is
 * the entire reason the model uses shares rather than percentages.
 *
 * The exchange total is required, and the backend marks the account to it
 * before pricing anything. See `PoolValueNowField` for why that is a hard
 * block rather than a suggestion.
 */
export function RecordSubscriptionDialog({ pool, open, onOpenChange }: Props) {
  const [apiError, setApiError] = useState<string | null>(null);
  const { mutateAsync, isPending } = useRecordSubscription(pool.id);

  const roster = pool.participants.filter((p) => !p.isArchived);

  const {
    handleSubmit,
    register,
    reset,
    setValue,
    watch,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { participantId: '', poolValueNow: 0, cash: 0, notes: '' },
  });

  const participantId = watch('participantId');

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    setApiError(null);
    try {
      await mutateAsync({
        participantId: parsed.participantId,
        poolValueNow: parsed.poolValueNow,
        cash: parsed.cash,
        notes: parsed.notes && parsed.notes.length > 0 ? parsed.notes : null,
      });
      toast.success('Money in recorded');
      reset();
      onOpenChange(false);
    } catch (err) {
      if (err instanceof ApiError) {
        setApiError(err.message);
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to record money in');
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next);
        if (!next) {
          reset();
          setApiError(null);
        }
      }}
    >
      <DialogContent
        className="max-h-[90vh] overflow-y-auto"
        data-testid="record-subscription-dialog"
      >
        <DialogHeader>
          <DialogTitle>Record money in</DialogTitle>
          <DialogDescription data-testid="record-subscription-context">
            Someone put money into {pool.name}. Their share is priced at the pool&rsquo;s value the
            moment it landed, so nobody else&rsquo;s share moves.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="subscription-participant">From</Label>
            <Select
              value={participantId}
              onValueChange={(v) => setValue('participantId', v, { shouldValidate: true })}
            >
              <SelectTrigger
                id="subscription-participant"
                data-testid="subscription-participant-select"
              >
                <SelectValue placeholder="Pick a participant" />
              </SelectTrigger>
              <SelectContent>
                {roster.map((p) => (
                  <SelectItem
                    key={p.id}
                    value={p.id}
                    data-testid={`subscription-participant-option-${p.name}`}
                  >
                    {p.name}
                    {p.isOwner ? ' (you)' : ''}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {errors.participantId && (
              <p
                className="text-xs text-destructive"
                role="alert"
                data-testid="subscription-participant-error"
              >
                {errors.participantId.message}
              </p>
            )}
          </div>

          <PoolValueNowField
            id="subscription-pool-value"
            currency={pool.currency}
            registration={register('poolValueNow')}
            error={errors.poolValueNow?.message}
          />

          <div className="space-y-2">
            <Label htmlFor="subscription-cash">Amount in ({pool.currency})</Label>
            <Input
              id="subscription-cash"
              data-testid="subscription-cash-input"
              type="number"
              step="0.01"
              min="0"
              {...register('cash')}
              aria-invalid={Boolean(errors.cash)}
            />
            <p className="text-xs text-muted-foreground">
              Recorded as a real movement on {pool.accountName}. The app currently reads it at{' '}
              {formatMoney(pool.accountBalance, pool.currency)} before this.
            </p>
            {errors.cash && (
              <p
                className="text-xs text-destructive"
                role="alert"
                data-testid="subscription-cash-error"
              >
                {errors.cash.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="subscription-notes">Notes (optional)</Label>
            <Textarea
              id="subscription-notes"
              data-testid="subscription-notes-input"
              maxLength={500}
              {...register('notes')}
            />
            {errors.notes && (
              <p className="text-xs text-destructive" role="alert">
                {errors.notes.message}
              </p>
            )}
          </div>

          {apiError && (
            <p
              className="text-sm text-destructive"
              role="alert"
              data-testid="record-subscription-error"
            >
              {apiError}
            </p>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="subscription-submit-button">
              {isPending ? 'Recording...' : 'Record money in'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
