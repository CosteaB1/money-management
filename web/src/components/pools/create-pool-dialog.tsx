'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useFieldArray, useForm } from 'react-hook-form';
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
import { Switch } from '@/src/components/ui/switch';
import { Textarea } from '@/src/components/ui/textarea';
import { useAccounts } from '@/src/lib/api/accounts';
import { ApiError } from '@/src/lib/api/client';
import { useCreatePool } from '@/src/lib/api/pools';
import { cn } from '@/src/lib/utils/cn';
import { formatMoney } from '@/src/lib/utils/currency';
import { todayIsoUtc } from '@/src/lib/utils/date';
import { roundPoolMoney } from '@/src/lib/utils/pool';
import type { AccountType, BackdatedSubscriptionRequest } from '@/src/types/api';

const today = () => todayIsoUtc();

/**
 * The account must be one that can take a balance adjustment — the same list
 * `AdjustBalanceCommandHandler.EligibleTypes` enforces server-side. An account
 * that can never be re-priced can never be re-marked, so its NAV would freeze
 * at inception and every later subscription would be struck at a dead price.
 */
const POOLABLE_TYPES: ReadonlySet<AccountType> = new Set([
  'Brokerage',
  'CryptoExchange',
  'P2PLending',
  'BankDeposit',
]);

const schema = z.object({
  accountId: z.string().uuid('Pick the account the pool lives in'),
  name: z
    .string()
    .trim()
    .min(1, 'Pool name is required')
    .max(100, 'Pool name must be 100 characters or less'),
  ownerName: z
    .string()
    .trim()
    .min(1, 'Your name is required')
    .max(100, 'Name must be 100 characters or less'),
  inceptionDate: z
    .string()
    .regex(/^\d{4}-\d{2}-\d{2}$/, 'Start date is required')
    .refine((v) => v <= today(), { message: 'Start date cannot be in the future' }),
  poolValueAtInception: z.coerce
    .number({ invalid_type_error: 'The total must be a number' })
    .positive('Type the exchange total on the start date'),
  notes: z.string().trim().max(500, 'Notes must be 500 characters or less').optional(),
  backdatedSubscriptions: z.array(
    z.object({
      participantName: z
        .string()
        .trim()
        .min(1, 'Name is required')
        .max(100, 'Name must be 100 characters or less'),
      occurredOn: z
        .string()
        .regex(/^\d{4}-\d{2}-\d{2}$/, 'Arrival date is required')
        .refine((v) => v <= today(), { message: 'Arrival date cannot be in the future' }),
      cash: z.coerce
        .number({ invalid_type_error: 'Amount must be a number' })
        .positive('Amount must be greater than 0'),
      poolValuePreMoney: z.coerce
        .number({ invalid_type_error: 'The total must be a number' })
        .positive('Type the total from just BEFORE their money landed'),
      writeMovementTransaction: z.boolean(),
    }),
  ),
});

type FormValues = z.input<typeof schema>;
type ParsedValues = z.output<typeof schema>;

/**
 * `writeMovementTransaction` defaults to **true**, and that default is the
 * whole point of the back-fill flow.
 *
 * The flow exists for money that landed on the exchange and was never entered
 * into the app; money that is already sitting there as a transaction is the
 * rare case. Defaulted the other way, a missed click issues the participant
 * their share while writing nothing to the account, so the pool ends up holding
 * more than the balance backs — silently, with no error and nothing on screen.
 * That has already happened once in production (3,000 units against a 2,000
 * balance, NAV 0.667). Leaving it OFF is now the deliberate act.
 */
const EMPTY_BACKFILL = {
  participantName: '',
  occurredOn: today(),
  cash: 0,
  poolValuePreMoney: 0,
  writeMovementTransaction: true,
};

/** One back-fill row as the consequence summary sees it: raw form state. */
export interface BackfillRowSnapshot {
  /** `useFieldArray`'s key — carried through so the summary can key its list. */
  id: string;
  participantName: string | undefined;
  /** Straight off a number input, so a string far more often than a number. */
  cash: unknown;
  writeMovementTransaction: boolean | undefined;
}

/** A single arrival, resolved to a name and an amount. */
export interface BackfillArrival {
  id: string;
  name: string;
  cash: number;
}

/** What creating the pool will do to the account, in money terms. */
export interface BackfillConsequence {
  /** Rows that each write a deposit onto the account. */
  written: BackfillArrival[];
  /** Rows the user has declared are already on the account. */
  assumed: BackfillArrival[];
  writtenTotal: number;
  /** Exactly how much more than the balance the pool would hold, if wrong. */
  assumedTotal: number;
  /** The exchange total typed for the start date — where the account starts. */
  opening: number;
  /** Where the account lands once the written arrivals are recorded. */
  closing: number;
}

/** Number inputs hand back strings, blanks and the odd `undefined`. */
function toAmount(value: unknown): number {
  const parsed = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

/**
 * Projects the account balance the user is about to commit to.
 *
 * Safe to state as a plain fact because the domain refuses an arrival dated
 * before inception (`PoolUnitEvent.Create`), and the inception mark sets the
 * account to `poolValueAtInception` **as of that date**. So every written
 * arrival lands strictly after the mark and simply adds on top of it.
 *
 * Only the ticked rows move the total. That is deliberate: un-ticking a row has
 * to visibly change the number, or the summary is decoration rather than a
 * check.
 */
export function projectBackfills(
  rows: readonly BackfillRowSnapshot[],
  poolValueAtInception: unknown,
): BackfillConsequence {
  const written: BackfillArrival[] = [];
  const assumed: BackfillArrival[] = [];

  for (const row of rows) {
    const name = row.participantName?.trim();
    const arrival: BackfillArrival = {
      id: row.id,
      name: name && name.length > 0 ? name : 'Unnamed arrival',
      cash: toAmount(row.cash),
    };
    (row.writeMovementTransaction ? written : assumed).push(arrival);
  }

  const total = (arrivals: readonly BackfillArrival[]) =>
    roundPoolMoney(arrivals.reduce((sum, a) => sum + a.cash, 0));

  const writtenTotal = total(written);
  const opening = toAmount(poolValueAtInception);

  return {
    written,
    assumed,
    writtenTotal,
    assumedTotal: total(assumed),
    opening,
    closing: roundPoolMoney(opening + writtenTotal),
  };
}

/**
 * Creates a pool over an existing account and seeds the owner.
 *
 * Three things this dialog refuses to guess:
 *
 * 1. **The exchange total on the start date is required**, even though the
 *    backend allows it to be null. Creating the pool turns the account's
 *    balance into the owner's opening share; if that balance is stale, every
 *    share issued afterwards is priced against a number that was never true.
 *    A total that matches what the app already derived writes no row at all
 *    (a zero delta is skipped, not an error), so typing it costs nothing.
 *
 * 2. **Money that arrived before the pool existed needs its own pre-money
 *    total**, one per arrival. The account's balance on that date already
 *    contains their cash — defaulting it would price the participant against
 *    their own money and hand them a slice of it for free.
 *
 * 3. **Whether each arrival still has to be recorded on the account.** Getting
 *    this wrong is not a validation error — it is a pool that quietly holds
 *    more than its balance backs — so the answer is defaulted to the common
 *    case and the resulting balance is spelled out before the user can submit.
 */
export function CreatePoolDialog() {
  const [open, setOpen] = useState(false);
  const [apiError, setApiError] = useState<string | null>(null);
  const [hasBackfills, setHasBackfills] = useState(false);
  const accountsQuery = useAccounts(false);
  const { mutateAsync, isPending } = useCreatePool();

  const {
    control,
    handleSubmit,
    register,
    reset,
    setValue,
    watch,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      accountId: '',
      name: '',
      ownerName: 'Me',
      inceptionDate: today(),
      poolValueAtInception: 0,
      notes: '',
      backdatedSubscriptions: [],
    },
  });

  const { fields, append, remove, replace } = useFieldArray({
    control,
    name: 'backdatedSubscriptions',
  });

  const accountId = watch('accountId');
  const inceptionDate = watch('inceptionDate');
  const backfillValues = watch('backdatedSubscriptions');
  const poolValueAtInception = watch('poolValueAtInception');

  // Only non-archived, re-priceable, not-already-pooled accounts qualify —
  // `pools.account_id` is unique, so offering a pooled account guarantees a
  // 409 the user can do nothing about.
  const eligible = (accountsQuery.data ?? []).filter(
    (a) => !a.isArchived && !a.isPooled && POOLABLE_TYPES.has(a.type),
  );
  const selected = eligible.find((a) => a.id === accountId);
  // The pool never does FX: its currency IS the account's, so it is shown
  // rather than chosen.
  const currency = selected?.currency ?? '';
  const accountLabel = selected?.name ?? 'the account';

  // Before an account is picked there is no currency to name, and defaulting to
  // one would put a confident "MDL" next to a figure that is not in MDL.
  const money = (amount: number) =>
    currency
      ? formatMoney(amount, currency)
      : amount.toLocaleString('ro-MD', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  // `fields` carries the stable keys, `watch` carries the live values — the
  // documented pairing for a field array whose values drive rendering. The
  // toggle below is read out of form state rather than left to the DOM, so a
  // row's answer cannot drift from what will be submitted.
  const rows: BackfillRowSnapshot[] = fields.map((field, index) => ({
    id: field.id,
    participantName: backfillValues?.[index]?.participantName,
    cash: backfillValues?.[index]?.cash,
    writeMovementTransaction: backfillValues?.[index]?.writeMovementTransaction,
  }));
  const consequence = projectBackfills(rows, poolValueAtInception);

  const toggleBackfills = (next: boolean) => {
    setHasBackfills(next);
    if (next) {
      replace([{ ...EMPTY_BACKFILL }]);
    } else {
      // Rows left behind would still be validated and still be sent.
      replace([]);
    }
  };

  const closeAndReset = () => {
    reset();
    setApiError(null);
    setHasBackfills(false);
  };

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    setApiError(null);
    const backfills: BackdatedSubscriptionRequest[] = parsed.backdatedSubscriptions.map((s) => ({
      participantName: s.participantName,
      occurredOn: s.occurredOn,
      cash: s.cash,
      poolValuePreMoney: s.poolValuePreMoney,
      writeMovementTransaction: s.writeMovementTransaction,
    }));

    try {
      await mutateAsync({
        accountId: parsed.accountId,
        name: parsed.name,
        currency,
        inceptionDate: parsed.inceptionDate,
        ownerName: parsed.ownerName,
        poolValueAtInception: parsed.poolValueAtInception,
        notes: parsed.notes && parsed.notes.length > 0 ? parsed.notes : null,
        backdatedSubscriptions: backfills.length > 0 ? backfills : null,
      });
      toast.success('Pool created');
      closeAndReset();
      setOpen(false);
    } catch (err) {
      if (err instanceof ApiError) {
        setApiError(err.message);
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to create pool');
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) closeAndReset();
      }}
    >
      <DialogTrigger asChild>
        <Button data-testid="add-pool-button">
          <Plus className="h-4 w-4" />
          Add pool
        </Button>
      </DialogTrigger>
      <DialogContent
        className="max-h-[90vh] overflow-y-auto sm:max-w-2xl"
        data-testid="create-pool-dialog"
      >
        <DialogHeader>
          <DialogTitle>Add pool</DialogTitle>
          <DialogDescription>
            Track an account that holds other people&rsquo;s money alongside your own. Everyone, you
            included, holds a share; the account keeps showing its full balance, while your net
            worth only counts your part of it.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="pool-account">Account</Label>
            <Select
              value={accountId}
              onValueChange={(v) => setValue('accountId', v, { shouldValidate: true })}
            >
              <SelectTrigger id="pool-account" data-testid="pool-account-select">
                <SelectValue placeholder="Pick an account" />
              </SelectTrigger>
              <SelectContent>
                {eligible.map((a) => (
                  <SelectItem key={a.id} value={a.id} data-testid={`pool-account-option-${a.name}`}>
                    {a.name} ({a.currency})
                  </SelectItem>
                ))}
                {eligible.length === 0 && (
                  <div className="px-2 py-1.5 text-sm text-muted-foreground">
                    No eligible accounts — a pool needs an investment account that can be re-priced
                    and does not already have one.
                  </div>
                )}
              </SelectContent>
            </Select>
            <p className="text-xs text-muted-foreground">
              The pool is denominated in the account&rsquo;s own currency
              {currency ? ` (${currency})` : ''} and never converts.
            </p>
            {errors.accountId && (
              <p className="text-xs text-destructive" role="alert" data-testid="pool-account-error">
                {errors.accountId.message}
              </p>
            )}
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="pool-name">Pool name</Label>
              <Input
                id="pool-name"
                data-testid="pool-name-input"
                maxLength={100}
                placeholder="e.g. Binance pool"
                {...register('name')}
                aria-invalid={Boolean(errors.name)}
              />
              {errors.name && (
                <p className="text-xs text-destructive" role="alert" data-testid="pool-name-error">
                  {errors.name.message}
                </p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="pool-owner-name">Your name in the pool</Label>
              <Input
                id="pool-owner-name"
                data-testid="pool-owner-name-input"
                maxLength={100}
                {...register('ownerName')}
                aria-invalid={Boolean(errors.ownerName)}
              />
              {errors.ownerName && (
                <p className="text-xs text-destructive" role="alert">
                  {errors.ownerName.message}
                </p>
              )}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="pool-inception">Start date</Label>
            <Input
              id="pool-inception"
              data-testid="pool-inception-input"
              type="date"
              max={today()}
              {...register('inceptionDate')}
              aria-invalid={Boolean(errors.inceptionDate)}
            />
            {errors.inceptionDate && (
              <p className="text-xs text-destructive" role="alert">
                {errors.inceptionDate.message}
              </p>
            )}
          </div>

          <div className="space-y-2 rounded-md border border-amber-500/40 bg-amber-500/5 p-3">
            <Label htmlFor="pool-inception-value" className="text-sm font-semibold">
              Exchange total on {inceptionDate} {currency ? `(${currency})` : ''} *
            </Label>
            <Input
              id="pool-inception-value"
              data-testid="pool-inception-value-input"
              type="number"
              step="0.01"
              min="0"
              inputMode="decimal"
              {...register('poolValueAtInception')}
              aria-invalid={Boolean(errors.poolValueAtInception)}
              aria-describedby="pool-inception-value-help"
            />
            <p id="pool-inception-value-help" className="text-xs text-muted-foreground">
              The account&rsquo;s <strong>whole</strong> real total on that date — spot, futures,
              earn, fiat, <strong>including BNB</strong>. This becomes your opening share, so a
              stale figure misprices everything issued afterwards. If it matches what the app
              already derived, nothing is written.
            </p>
            {errors.poolValueAtInception && (
              <p
                className="text-xs text-destructive"
                role="alert"
                data-testid="pool-inception-value-error"
              >
                {errors.poolValueAtInception.message}
              </p>
            )}
          </div>

          <div className="space-y-3 rounded-md border p-3">
            <div className="flex items-center justify-between gap-3">
              <Label htmlFor="pool-backfill-toggle" className="text-sm font-medium">
                Money already arrived before today
              </Label>
              <Switch
                id="pool-backfill-toggle"
                data-testid="pool-backfill-toggle"
                checked={hasBackfills}
                onCheckedChange={toggleBackfills}
              />
            </div>
            <p className="text-xs text-muted-foreground">
              Record each arrival at the price that applied on <em>its own</em> day. You need the
              exchange total from <strong>just before</strong> their money landed — the balance on
              that date already includes it, so anything else prices them against their own money.
            </p>

            {hasBackfills &&
              rows.map((row, index) => (
                <div
                  key={row.id}
                  className="space-y-3 rounded-md border bg-muted/20 p-3"
                  data-testid="pool-backfill-row"
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                      Arrival {index + 1}
                    </span>
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onClick={() => remove(index)}
                      data-testid={`backfill-remove-${index}`}
                    >
                      <Trash2 className="h-4 w-4" />
                      Remove
                    </Button>
                  </div>
                  <div className="grid gap-2 sm:grid-cols-2">
                    <div className="space-y-1">
                      <Label htmlFor={`backfill-name-${index}`} className="text-xs">
                        Who
                      </Label>
                      <Input
                        id={`backfill-name-${index}`}
                        data-testid={`backfill-name-input-${index}`}
                        maxLength={100}
                        {...register(`backdatedSubscriptions.${index}.participantName`)}
                      />
                      {errors.backdatedSubscriptions?.[index]?.participantName && (
                        <p className="text-xs text-destructive" role="alert">
                          {errors.backdatedSubscriptions[index]?.participantName?.message}
                        </p>
                      )}
                    </div>
                    <div className="space-y-1">
                      <Label htmlFor={`backfill-date-${index}`} className="text-xs">
                        Arrived on
                      </Label>
                      <Input
                        id={`backfill-date-${index}`}
                        data-testid={`backfill-date-input-${index}`}
                        type="date"
                        max={today()}
                        {...register(`backdatedSubscriptions.${index}.occurredOn`)}
                      />
                      {errors.backdatedSubscriptions?.[index]?.occurredOn && (
                        <p className="text-xs text-destructive" role="alert">
                          {errors.backdatedSubscriptions[index]?.occurredOn?.message}
                        </p>
                      )}
                    </div>
                    <div className="space-y-1">
                      <Label htmlFor={`backfill-cash-${index}`} className="text-xs">
                        Amount {currency ? `(${currency})` : ''}
                      </Label>
                      <Input
                        id={`backfill-cash-${index}`}
                        data-testid={`backfill-cash-input-${index}`}
                        type="number"
                        step="0.01"
                        min="0"
                        {...register(`backdatedSubscriptions.${index}.cash`)}
                      />
                      {errors.backdatedSubscriptions?.[index]?.cash && (
                        <p className="text-xs text-destructive" role="alert">
                          {errors.backdatedSubscriptions[index]?.cash?.message}
                        </p>
                      )}
                    </div>
                    <div className="space-y-1">
                      <Label htmlFor={`backfill-pre-${index}`} className="text-xs">
                        Exchange total just before it landed
                      </Label>
                      <Input
                        id={`backfill-pre-${index}`}
                        data-testid={`backfill-pre-input-${index}`}
                        type="number"
                        step="0.01"
                        min="0"
                        {...register(`backdatedSubscriptions.${index}.poolValuePreMoney`)}
                      />
                      {errors.backdatedSubscriptions?.[index]?.poolValuePreMoney && (
                        <p
                          className="text-xs text-destructive"
                          role="alert"
                          data-testid={`backfill-pre-error-${index}`}
                        >
                          {errors.backdatedSubscriptions[index]?.poolValuePreMoney?.message}
                        </p>
                      )}
                    </div>
                  </div>
                  {/* Deliberately the heaviest thing in the row. It used to be
                      a bare checkbox in muted 12px under four money fields, and
                      a single missed click on it corrupted a real pool. */}
                  <div
                    className={cn(
                      'flex items-start justify-between gap-3 rounded-md border p-3',
                      row.writeMovementTransaction
                        ? 'bg-muted/40'
                        : 'border-amber-500/50 bg-amber-500/10',
                    )}
                  >
                    <div className="space-y-1">
                      <Label
                        htmlFor={`backfill-write-tx-${index}`}
                        className="text-sm font-semibold"
                      >
                        Write this arrival to {accountLabel}
                      </Label>
                      <p
                        id={`backfill-write-tx-help-${index}`}
                        className="text-xs text-muted-foreground"
                        data-testid={`backfill-write-tx-help-${index}`}
                      >
                        {row.writeMovementTransaction
                          ? `Records ${money(toAmount(row.cash))} onto ${accountLabel} on the arrival date. This is the usual case: the money reached the exchange but was never entered here.`
                          : `Nothing will be recorded. The app takes it that ${money(toAmount(row.cash))} is already on ${accountLabel} as a transaction on the arrival date. Their share is issued either way.`}
                      </p>
                    </div>
                    <Switch
                      id={`backfill-write-tx-${index}`}
                      data-testid={`backfill-write-tx-${index}`}
                      checked={Boolean(row.writeMovementTransaction)}
                      aria-describedby={`backfill-write-tx-help-${index}`}
                      onCheckedChange={(next) =>
                        setValue(`backdatedSubscriptions.${index}.writeMovementTransaction`, next)
                      }
                    />
                  </div>
                </div>
              ))}

            {hasBackfills && (
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => append({ ...EMPTY_BACKFILL })}
                data-testid="backfill-add"
              >
                <Plus className="h-4 w-4" />
                Add another arrival
              </Button>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="pool-notes">Notes (optional)</Label>
            <Textarea
              id="pool-notes"
              data-testid="pool-notes-input"
              maxLength={500}
              {...register('notes')}
            />
            {errors.notes && (
              <p className="text-xs text-destructive" role="alert">
                {errors.notes.message}
              </p>
            )}
          </div>

          {/* The last thing read before Create, and the only place the two
              numbers that must agree — units issued and money on the account —
              are stated together. A user who sees the wrong closing total here
              catches the mistake before it is written. */}
          {hasBackfills && rows.length > 0 && (
            <div
              className="space-y-2 rounded-md border bg-muted/30 p-3"
              data-testid="pool-consequence"
            >
              <p className="text-sm font-semibold">What this will do</p>
              <p className="text-xs text-muted-foreground" data-testid="pool-consequence-written">
                {consequence.written.length === 0
                  ? `No arrival will be recorded on ${accountLabel}, so its total stays at ${money(consequence.opening)}.`
                  : `This will record ${
                      consequence.written.length === 1
                        ? `1 arrival of ${money(consequence.writtenTotal)}`
                        : `${consequence.written.length} arrivals totalling ${money(consequence.writtenTotal)}`
                    } on ${accountLabel}, taking it from ${money(consequence.opening)} to ${money(consequence.closing)}.`}
              </p>
              {consequence.assumed.length > 0 && (
                <div
                  className="space-y-1 rounded-md border border-amber-500/50 bg-amber-500/10 p-2"
                  data-testid="pool-consequence-assumed"
                >
                  <p className="text-xs font-medium">
                    Not recorded — the app expects this money is already on {accountLabel}:
                  </p>
                  <ul className="space-y-0.5 text-xs text-muted-foreground">
                    {consequence.assumed.map((arrival) => (
                      <li key={arrival.id}>
                        {arrival.name} — {money(arrival.cash)}
                      </li>
                    ))}
                  </ul>
                  <p className="text-xs text-muted-foreground">
                    Their share is issued either way, so if it is not already there the pool will
                    hold {money(consequence.assumedTotal)} more than the balance backs.
                  </p>
                </div>
              )}
            </div>
          )}

          {apiError && (
            <p className="text-sm text-destructive" role="alert" data-testid="create-pool-error">
              {apiError}
            </p>
          )}

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => {
                // Cancel is a discard, so it goes through the same teardown the
                // overlay does — clicking the trigger again must not resurrect a
                // half-typed backfill row.
                closeAndReset();
                setOpen(false);
              }}
            >
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="pool-submit-button">
              {isPending ? 'Creating...' : 'Create pool'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
