'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { Info } from 'lucide-react';
import type { ReactNode } from 'react';
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
import { Label } from '@/src/components/ui/label';
import { Textarea } from '@/src/components/ui/textarea';
import { ApiError } from '@/src/lib/api/client';
import { useCloseDistribution } from '@/src/lib/api/pools';
import { formatMoney } from '@/src/lib/utils/currency';
import { previewPoolValuation, roundPoolMoney } from '@/src/lib/utils/pool';
import type { DistributionPayoutRequest, PoolDetailDto } from '@/src/types/api';

const schema = z.object({
  poolValueNow: z.coerce
    .number({ invalid_type_error: 'The exchange total must be a number' })
    .positive('Type the exchange total before closing the month'),
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
 * Closes the month.
 *
 * **Close and pay are two separate steps and this dialog must not blur them.**
 * Closing marks the pool, retires shares at that price and records what is
 * owed. *No money moves.* The transfer is settled later, on the day the cash
 * actually leaves — date the payout at the close and the next month's snapshot
 * still contains that cash, so the app re-attributes it as fresh profit and
 * pays everyone twice on the same money, every month.
 *
 * Amounts are never typed here. The dialog only chooses **who** gets paid, and
 * the backend computes each amount from the freshly-marked value at the
 * moment of the close: the figures the API sent were priced against the
 * *previous* mark, and an explicit amount above someone's distributable would
 * hand back principal and quietly reset their high-water mark. Leaving someone
 * unchecked lets their profit compound instead — which is exactly what "don't
 * send mine this month" means, with no extra bookkeeping.
 *
 * **The preview and the gate, however, must NOT use those server figures.** A
 * close sweeps every participant's stake back to their capital base, so after
 * any close everybody's `distributable` is ~0 until the account is marked
 * again. Gating the button on that number made the primary monthly workflow
 * unreachable: the user typed a total that plainly pays their friends, and the
 * dialog answered "nothing is payable" and stayed disabled. Everything below
 * is therefore repriced against the typed total via `previewPoolValuation`,
 * which mirrors `PoolUnitRegister.SnapshotAsOf`. Preview and gate only — the
 * server stays the authority on the amounts.
 */
export function CloseMonthDialog({ pool, open, onOpenChange }: Props) {
  const [apiError, setApiError] = useState<string | null>(null);
  const [excluded, setExcluded] = useState<ReadonlySet<string>>(new Set());
  const { mutateAsync, isPending } = useCloseDistribution(pool.id);

  const {
    handleSubmit,
    register,
    reset,
    watch,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { poolValueNow: 0, notes: '' },
  });

  // A number input hands back a string; before the first keystroke it is the
  // `0` default. Both mean "no mark typed yet".
  const typedRaw = watch('poolValueNow');
  const typedTotal = Number.parseFloat(String(typedRaw ?? ''));
  const hasTypedTotal = Number.isFinite(typedTotal) && typedTotal > 0;

  // Until they type, price against the account's last known balance — that
  // reproduces the API's own figures, so the roster is legible on open. From
  // the first keystroke the typed total is the mark, and everything below
  // follows it.
  const preview = previewPoolValuation(pool, hasTypedTotal ? typedTotal : pool.accountBalance);

  // The owner is never in the payout set: their profit stays in and grows their
  // share, and they take value out with a redemption instead. A seed carries no
  // cash, so the owner's "distributable" is their whole stake — a number that
  // must never become a button. The server's close excludes them too.
  const rows = pool.participants
    .filter((p) => !p.isOwner && !p.isArchived)
    .map((participant) => {
      const amount = preview.positions.get(participant.id)?.distributable ?? 0;
      // Payable at the scale money is actually paid at: a third of a cent is
      // not a payout, and the server would floor it away anyway.
      return { participant, amount, canPay: roundPoolMoney(amount) > 0 };
    });

  const payable = rows.filter((row) => row.canPay);
  const nothingPayable = payable.length === 0;
  const included = payable.filter((row) => !excluded.has(row.participant.id));
  const totalOwed = included.reduce((sum, row) => sum + row.amount, 0);

  const toggle = (id: string) => {
    setExcluded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const closeAndReset = () => {
    reset();
    setApiError(null);
    setExcluded(new Set());
  };

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    setApiError(null);

    // No exclusions → omit `payouts` entirely and let the server pay every
    // outside participant their full distributable. Sending an explicit list
    // that happens to match would behave identically but bind the request to
    // figures computed here rather than at the close.
    const payouts: DistributionPayoutRequest[] | null =
      excluded.size === 0 ? null : included.map((row) => ({ participantId: row.participant.id }));

    try {
      const result = await mutateAsync({
        poolValueNow: parsed.poolValueNow,
        payouts,
        notes: parsed.notes && parsed.notes.length > 0 ? parsed.notes : null,
      });
      toast.success(
        `Month closed — ${formatMoney(result.totalCash, pool.currency)} owed across ${
          result.lines.length
        } payout${result.lines.length === 1 ? '' : 's'}. Nothing has been sent yet.`,
      );
      closeAndReset();
      onOpenChange(false);
    } catch (err) {
      if (err instanceof ApiError) {
        setApiError(err.message);
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to close the month');
    }
  });

  // "Nothing payable" is a real, valuable answer — a green month genuinely can
  // pay zero — so the message stays. It just has to be TRUE, which means
  // saying which mark it is true of, and naming the actual reason when the
  // pool cannot be priced at all rather than blaming the high-water rule.
  let nothingPayableMessage: ReactNode;
  if (!hasTypedTotal) {
    nothingPayableMessage = (
      <>
        Nothing is payable at the pool&rsquo;s last known value — everyone is still at or below what
        they put in. Type this month&rsquo;s exchange total above and this recalculates against it.
        If it still pays nobody, record the month with <strong>Update balance</strong> on{' '}
        {pool.accountName}.
      </>
    );
  } else if (preview.navPerUnit === null) {
    nothingPayableMessage = (
      <>
        {pool.totalUnits > 0 ? (
          <>
            {formatMoney(typedTotal, pool.currency)} is at or below the{' '}
            {formatMoney(pool.unpaidDistributionCash, pool.currency)} already closed but not yet
            sent, so there is nothing left to price shares against.
          </>
        ) : (
          <>This pool holds no shares, so there is no price to strike and nobody to pay.</>
        )}{' '}
        To record the month&rsquo;s value anyway, use <strong>Update balance</strong> on{' '}
        {pool.accountName}.
      </>
    );
  } else {
    nothingPayableMessage = (
      <>
        At {formatMoney(typedTotal, pool.currency)} nothing is payable — everyone is still at or
        below what they put in, so a close would have no lines to write. That can happen after a
        green month, and it is correct. To record the month&rsquo;s value anyway, use{' '}
        <strong>Update balance</strong> on {pool.accountName}.
      </>
    );
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next);
        if (!next) closeAndReset();
      }}
    >
      <DialogContent className="max-h-[90vh] overflow-y-auto" data-testid="close-month-dialog">
        <DialogHeader>
          <DialogTitle>Close the month</DialogTitle>
          <DialogDescription>
            Records this month&rsquo;s value for {pool.name}, works out what each participant is
            owed and retires the matching shares.
          </DialogDescription>
        </DialogHeader>

        <div
          className="flex items-start gap-2 rounded-md border bg-muted/30 p-3 text-sm"
          data-testid="close-month-no-money-notice"
        >
          <Info className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden />
          <p>
            <strong>No money moves now.</strong> Closing only records what is owed. You settle each
            payout separately, on the day the transfer actually leaves — pay it out early and next
            month&rsquo;s total still contains that cash, which the app would read as fresh profit
            and pay out a second time.
          </p>
        </div>

        <form onSubmit={onSubmit} className="space-y-4">
          <PoolValueNowField
            id="close-pool-value"
            currency={pool.currency}
            registration={register('poolValueNow')}
            error={errors.poolValueNow?.message}
          />

          <div className="space-y-2">
            <p className="text-sm font-medium">Who gets paid</p>
            {nothingPayable ? (
              <p
                className="rounded-md border bg-muted/20 p-3 text-sm text-muted-foreground"
                data-testid="close-month-nothing-payable"
              >
                {nothingPayableMessage}
              </p>
            ) : (
              <>
                <ul
                  className="space-y-1 rounded-md border p-2"
                  data-testid="close-month-payout-list"
                >
                  {rows.map(({ participant, amount, canPay }) => (
                    <li
                      key={participant.id}
                      className="flex items-center justify-between gap-3 rounded px-2 py-1.5 text-sm"
                      data-testid="close-month-payout-row"
                    >
                      <label className="inline-flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={canPay && !excluded.has(participant.id)}
                          disabled={!canPay}
                          onChange={() => toggle(participant.id)}
                          data-testid={`close-month-payout-toggle-${participant.name}`}
                        />
                        <span>{participant.name}</span>
                      </label>
                      {canPay ? (
                        <span
                          className="tabular-nums"
                          data-testid={`close-month-payout-amount-${participant.name}`}
                        >
                          ~{formatMoney(amount, pool.currency)}
                        </span>
                      ) : (
                        <span
                          className="text-xs text-muted-foreground"
                          data-testid={`close-month-payout-none-${participant.name}`}
                        >
                          Nothing payable — still at or below what they put in
                        </span>
                      )}
                    </li>
                  ))}
                </ul>
                <p className="text-xs text-muted-foreground" data-testid="close-month-estimate">
                  About {formatMoney(totalOwed, pool.currency)} across {included.length} payout
                  {included.length === 1 ? '' : 's'},{' '}
                  {hasTypedTotal ? (
                    <>worked out from the {formatMoney(typedTotal, pool.currency)} typed above</>
                  ) : (
                    <>
                      based on the pool&rsquo;s last known value — type this month&rsquo;s exchange
                      total above and these follow it
                    </>
                  )}
                  . Every amount is recomputed by the app at the moment of the close, so the final
                  figures can land a cent either side of these. Unchecking somebody leaves their
                  profit in the pool to compound — they can take it any later month.
                </p>
              </>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="close-notes">Notes (optional)</Label>
            <Textarea
              id="close-notes"
              data-testid="close-notes-input"
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
            <p className="text-sm text-destructive" role="alert" data-testid="close-month-error">
              {apiError}
            </p>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button
              type="submit"
              disabled={isPending || nothingPayable || included.length === 0}
              data-testid="close-month-submit-button"
            >
              {isPending ? 'Closing...' : 'Close month'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
