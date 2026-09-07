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
import { useAccounts } from '@/src/lib/api/accounts';
import { ApiError } from '@/src/lib/api/client';
import { useRecordRedemption } from '@/src/lib/api/pools';
import { formatMoney } from '@/src/lib/utils/currency';
import { previewPoolValuation } from '@/src/lib/utils/pool';
import type { PoolDetailDto } from '@/src/types/api';

// Radix Select items can't carry an empty-string value, so "the money left
// for somewhere the app doesn't track" rides on a sentinel.
const NO_DESTINATION = 'none';

const schema = z.object({
  participantId: z.string().uuid('Pick whose money is leaving'),
  poolValueNow: z.coerce
    .number({ invalid_type_error: 'The exchange total must be a number' })
    .positive('Type the exchange total before recording money out'),
  cash: z.coerce
    .number({ invalid_type_error: 'Amount must be a number' })
    .positive('Amount must be greater than 0'),
  destinationAccountId: z.string().uuid('Pick a valid account').optional(),
  isFullWindDown: z.boolean(),
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
 * Money out. Burns shares at the prevailing price, which again leaves everyone
 * else untouched — an exit, a top-up withdrawal and the owner moving their own
 * profit to another exchange are all the same operation.
 *
 * `destinationAccountId` writes the receiving leg of a two-leg transfer. Value
 * only genuinely leaves the pool when it lands somewhere the app tracks;
 * shuffling between sub-wallets on the same exchange is invisible here by
 * design, because the pool is the whole account as one number.
 */
export function RecordRedemptionDialog({ pool, open, onOpenChange }: Props) {
  const [apiError, setApiError] = useState<string | null>(null);
  const accountsQuery = useAccounts(false);
  const { mutateAsync, isPending } = useRecordRedemption(pool.id);

  const roster = pool.participants.filter((p) => !p.isArchived);

  // Same-currency only, never the pool's own account, and never another
  // pooled account — cash arriving there without a unit event would be shared
  // pro-rata with that pool's participants.
  const destinations = (accountsQuery.data ?? []).filter(
    (a) => !a.isArchived && !a.isPooled && a.id !== pool.accountId && a.currency === pool.currency,
  );

  const {
    handleSubmit,
    register,
    reset,
    setValue,
    watch,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      participantId: '',
      poolValueNow: 0,
      cash: 0,
      destinationAccountId: undefined,
      isFullWindDown: false,
      notes: '',
    },
  });

  const participantId = watch('participantId');
  const destinationAccountId = watch('destinationAccountId');
  const selected = roster.find((p) => p.id === participantId);

  // The hint below is the number the user sizes the withdrawal against, and it
  // sits directly under "exchange total right now" — so it cannot keep quoting
  // the stake the API priced against the PREVIOUS mark. Type a total lower than
  // that mark and the stale figure over-states the share, which the server
  // rejects with "that would overdraw their share"; type a higher one and the
  // user leaves their own money behind. Repriced against the typed total, the
  // same way the close dialog is.
  const typedRaw = watch('poolValueNow');
  const typedTotal = Number.parseFloat(String(typedRaw ?? ''));
  const hasTypedTotal = Number.isFinite(typedTotal) && typedTotal > 0;
  const preview = hasTypedTotal ? previewPoolValuation(pool, typedTotal) : null;

  // Null is a real answer — no units or nothing left to price against means no
  // NAV, and a placeholder here would be a made-up valuation of somebody's
  // money. Quote nothing instead.
  const quotedStake =
    selected === undefined
      ? null
      : preview !== null
        ? (preview.positions.get(selected.id)?.stake ?? null)
        : selected.stake;

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    setApiError(null);
    try {
      await mutateAsync({
        participantId: parsed.participantId,
        poolValueNow: parsed.poolValueNow,
        cash: parsed.cash,
        destinationAccountId: parsed.destinationAccountId ?? null,
        isFullWindDown: parsed.isFullWindDown,
        notes: parsed.notes && parsed.notes.length > 0 ? parsed.notes : null,
      });
      toast.success('Money out recorded');
      reset();
      onOpenChange(false);
    } catch (err) {
      if (err instanceof ApiError) {
        setApiError(err.message);
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to record money out');
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
        data-testid="record-redemption-dialog"
      >
        <DialogHeader>
          <DialogTitle>Record money out</DialogTitle>
          <DialogDescription data-testid="record-redemption-context">
            Someone is taking money out of {pool.name}. Their share is cashed out at the
            pool&rsquo;s value right now, so nobody else&rsquo;s share moves.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="redemption-participant">Who is taking money out</Label>
            <Select
              value={participantId}
              onValueChange={(v) => setValue('participantId', v, { shouldValidate: true })}
            >
              <SelectTrigger
                id="redemption-participant"
                data-testid="redemption-participant-select"
              >
                <SelectValue placeholder="Pick a participant" />
              </SelectTrigger>
              <SelectContent>
                {roster.map((p) => (
                  <SelectItem
                    key={p.id}
                    value={p.id}
                    data-testid={`redemption-participant-option-${p.name}`}
                  >
                    {p.name}
                    {p.isOwner ? ' (you)' : ''}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {selected !== undefined && (
              <p className="text-xs text-muted-foreground" data-testid="redemption-stake-hint">
                {quotedStake === null ? (
                  <>
                    There is no price to value {selected.name}&rsquo;s share at, so there is no
                    figure to quote — the pool holds no shares, or nothing is left to price them
                    against.
                  </>
                ) : hasTypedTotal ? (
                  <>
                    At the {formatMoney(typedTotal, pool.currency)} typed above, {selected.name}
                    &rsquo;s share is worth about {formatMoney(quotedStake, pool.currency)}.
                  </>
                ) : (
                  <>
                    {selected.name} held {formatMoney(quotedStake, pool.currency)} at the
                    pool&rsquo;s last known value. Type the exchange total above and this reprices
                    against it.
                  </>
                )}
              </p>
            )}
            {errors.participantId && (
              <p
                className="text-xs text-destructive"
                role="alert"
                data-testid="redemption-participant-error"
              >
                {errors.participantId.message}
              </p>
            )}
          </div>

          <PoolValueNowField
            id="redemption-pool-value"
            currency={pool.currency}
            registration={register('poolValueNow')}
            error={errors.poolValueNow?.message}
          />

          <div className="space-y-2">
            <Label htmlFor="redemption-cash">Amount out ({pool.currency})</Label>
            <Input
              id="redemption-cash"
              data-testid="redemption-cash-input"
              type="number"
              step="0.01"
              min="0"
              {...register('cash')}
              aria-invalid={Boolean(errors.cash)}
            />
            {errors.cash && (
              <p
                className="text-xs text-destructive"
                role="alert"
                data-testid="redemption-cash-error"
              >
                {errors.cash.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="redemption-destination">Where it went (optional)</Label>
            <Select
              value={destinationAccountId ?? NO_DESTINATION}
              onValueChange={(v) =>
                setValue('destinationAccountId', v === NO_DESTINATION ? undefined : v, {
                  shouldValidate: true,
                })
              }
            >
              <SelectTrigger
                id="redemption-destination"
                data-testid="redemption-destination-select"
              >
                <SelectValue placeholder="Not tracked" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NO_DESTINATION} data-testid="redemption-destination-option-none">
                  Not tracked in this app
                </SelectItem>
                {destinations.map((a) => (
                  <SelectItem
                    key={a.id}
                    value={a.id}
                    data-testid={`redemption-destination-option-${a.name}`}
                  >
                    {a.name}
                  </SelectItem>
                ))}
                {destinations.length === 0 && (
                  <div className="px-2 py-1.5 text-sm text-muted-foreground">
                    No other {pool.currency} accounts available.
                  </div>
                )}
              </SelectContent>
            </Select>
            <p className="text-xs text-muted-foreground">
              Picking an account records the receiving half of the transfer, so the money is not
              lost between the two.
            </p>
          </div>

          {/* The looks-like-a-balance guard rejects an amount equal to the
              account's whole balance, because the demonstrated data-entry slip
              is a BALANCE typed into an AMOUNT field. Emptying the pool is the
              one case where those two are legitimately the same number, so it
              needs an explicit acknowledgement — which the backend then
              verifies against the ledger rather than trusting. */}
          <label className="flex items-start gap-2 rounded-md border p-3 text-sm">
            <input
              type="checkbox"
              className="mt-0.5"
              data-testid="redemption-wind-down-checkbox"
              {...register('isFullWindDown')}
            />
            <span>
              <span className="font-medium">This empties the pool completely</span>
              <span className="block text-xs text-muted-foreground">
                Tick only when this withdrawal takes everything that is left. Amounts equal to the
                account&rsquo;s whole balance are otherwise refused, because that is what a mistyped
                balance looks like. The app checks the claim: if anyone would still hold a share
                afterwards, it is rejected.
              </span>
            </span>
          </label>

          <div className="space-y-2">
            <Label htmlFor="redemption-notes">Notes (optional)</Label>
            <Textarea
              id="redemption-notes"
              data-testid="redemption-notes-input"
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
              data-testid="record-redemption-error"
            >
              {apiError}
            </p>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="redemption-submit-button">
              {isPending ? 'Recording...' : 'Record money out'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
