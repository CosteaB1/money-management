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
import { useSettleDistribution } from '@/src/lib/api/pools';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate, todayIsoUtc } from '@/src/lib/utils/date';
import type { PoolUnitEventDto } from '@/src/types/api';

const today = () => todayIsoUtc();

interface Props {
  poolId: string;
  poolCurrency: string;
  accountName: string;
  event: PoolUnitEventDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Phase two of a payout: the day the transfer physically left.
 *
 * The close already retired the shares and recorded the debt; this writes the
 * real transaction on the account. **The date matters more than it looks** —
 * it must be the day the cash actually moved, not the day the month closed.
 * Until this runs, the money is still sitting in the account, and the pool's
 * value deliberately excludes it so it is not counted as profit twice.
 */
export function SettlePayoutDialog({
  poolId,
  poolCurrency,
  accountName,
  event,
  open,
  onOpenChange,
}: Props) {
  const [apiError, setApiError] = useState<string | null>(null);
  const { mutateAsync, isPending } = useSettleDistribution(poolId);

  const schema = z.object({
    settledOn: z
      .string()
      .regex(/^\d{4}-\d{2}-\d{2}$/, 'Settlement date is required')
      .refine((v) => v <= today(), { message: 'Settlement date cannot be in the future' })
      .refine((v) => v >= event.occurredOn, {
        message: 'The transfer cannot have left before the month was closed',
      }),
    notes: z.string().trim().max(500, 'Notes must be 500 characters or less').optional(),
  });

  const {
    handleSubmit,
    register,
    reset,
    formState: { errors },
  } = useForm<z.input<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { settledOn: today(), notes: '' },
  });

  const onSubmit = handleSubmit(async (values) => {
    setApiError(null);
    try {
      await mutateAsync({
        eventId: event.id,
        settledOn: values.settledOn,
        notes: values.notes && values.notes.length > 0 ? values.notes : null,
      });
      toast.success(`Paid ${event.participantName}`);
      reset();
      onOpenChange(false);
    } catch (err) {
      if (err instanceof ApiError) {
        setApiError(err.message);
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to settle the payout');
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
      <DialogContent data-testid="settle-payout-dialog">
        <DialogHeader>
          <DialogTitle>Mark payout as sent</DialogTitle>
          <DialogDescription data-testid="settle-payout-context">
            {formatMoney(event.cash ?? 0, event.cashCurrency ?? poolCurrency)} owed to{' '}
            <strong>{event.participantName}</strong>, closed on {formatShortDate(event.occurredOn)}.
            This writes the real movement on {accountName} — use the date the transfer actually
            left, not the date the month closed.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="settle-date">Date it left</Label>
            <Input
              id="settle-date"
              data-testid="settle-date-input"
              type="date"
              min={event.occurredOn}
              max={today()}
              {...register('settledOn')}
              aria-invalid={Boolean(errors.settledOn)}
            />
            {errors.settledOn && (
              <p className="text-xs text-destructive" role="alert" data-testid="settle-date-error">
                {errors.settledOn.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="settle-notes">Notes (optional)</Label>
            <Textarea
              id="settle-notes"
              data-testid="settle-notes-input"
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
            <p className="text-sm text-destructive" role="alert" data-testid="settle-payout-error">
              {apiError}
            </p>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="settle-submit-button">
              {isPending ? 'Recording...' : 'Mark as sent'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
