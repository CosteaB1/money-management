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
import { Textarea } from '@/src/components/ui/textarea';
import { ApiError } from '@/src/lib/api/client';
import { useRecordCostReimbursement } from '@/src/lib/api/pools';
import { formatMoney } from '@/src/lib/utils/currency';
import type { PoolDetailDto } from '@/src/types/api';

const schema = z.object({
  amount: z.coerce
    .number({ invalid_type_error: 'Amount must be a number' })
    .positive('Amount must be greater than 0'),
  // Optional here and ONLY here — no cash crosses the pool boundary, so the
  // backend leaves it nullable. Zero is the "not supplied" sentinel rather
  // than a `.optional()`: a blank number input coerces to 0 anyway, and 0 is
  // never a legal pool value (the server rejects anything <= 0), so the two
  // meanings can never collide.
  poolValueNow: z.coerce
    .number({ invalid_type_error: 'The exchange total must be a number' })
    .min(0, 'The exchange total cannot be negative'),
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
 * A cost the OWNER paid out of pocket that the participants reimburse
 * pro-rata — a server bill, say.
 *
 * The money never enters the account, so this must not mint value out of
 * nothing. It is a **pure transfer of shares**: each participant's share
 * shrinks by their portion of the cost, the owner's grows by the same total.
 * The pool's value, its total shares and the price per share are all unchanged
 * — nobody is richer or poorer than the cost itself.
 *
 * Exchange trading fees are explicitly NOT recorded here: they are paid with
 * pool money on pool trades, so the exchange total already carries them, and
 * recording them again would deduct them twice.
 *
 * The exchange total is the one place in the app where it is optional, because
 * no cash crosses the boundary. Supplying it still re-prices the pool first,
 * so the transfer is struck at a fresh price — which is the better answer.
 */
export function RecordCostReimbursementDialog({ pool, open, onOpenChange }: Props) {
  const [apiError, setApiError] = useState<string | null>(null);
  const { mutateAsync, isPending } = useRecordCostReimbursement(pool.id);

  const {
    handleSubmit,
    register,
    reset,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { amount: 0, poolValueNow: 0, notes: '' },
  });

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    setApiError(null);
    try {
      const result = await mutateAsync({
        amount: parsed.amount,
        poolValueNow: parsed.poolValueNow > 0 ? parsed.poolValueNow : null,
        notes: parsed.notes && parsed.notes.length > 0 ? parsed.notes : null,
      });
      toast.success(
        `Recovered ${formatMoney(result.totalAmountRecovered, pool.currency)} across ${
          result.lines.length
        } participant${result.lines.length === 1 ? '' : 's'}`,
      );
      reset();
      onOpenChange(false);
    } catch (err) {
      if (err instanceof ApiError) {
        setApiError(err.message);
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to record the cost');
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
        data-testid="cost-reimbursement-dialog"
      >
        <DialogHeader>
          <DialogTitle>Record a shared cost</DialogTitle>
          <DialogDescription>
            A cost you paid from your own pocket that the pool should share. Nothing enters or
            leaves the account — everyone else&rsquo;s share shrinks by their portion of the cost
            and yours grows by the same amount, at today&rsquo;s price.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="cost-amount">Full cost ({pool.currency})</Label>
            <Input
              id="cost-amount"
              data-testid="cost-amount-input"
              type="number"
              step="0.01"
              min="0"
              {...register('amount')}
              aria-invalid={Boolean(errors.amount)}
            />
            <p className="text-xs text-muted-foreground">
              Type the whole cost. Everyone bears their share of it, you included — you only recover
              the part the others owe.
            </p>
            {errors.amount && (
              <p className="text-xs text-destructive" role="alert" data-testid="cost-amount-error">
                {errors.amount.message}
              </p>
            )}
          </div>

          <PoolValueNowField
            id="cost-pool-value"
            currency={pool.currency}
            registration={register('poolValueNow')}
            error={errors.poolValueNow?.message}
            optional
          />

          <div className="space-y-2">
            <Label htmlFor="cost-notes">Notes (optional)</Label>
            <Textarea
              id="cost-notes"
              data-testid="cost-notes-input"
              maxLength={500}
              placeholder="e.g. VPS, September"
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
              data-testid="cost-reimbursement-error"
            >
              {apiError}
            </p>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="cost-submit-button">
              {isPending ? 'Recording...' : 'Record cost'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
