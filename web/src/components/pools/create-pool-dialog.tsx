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
import { todayIsoUtc } from '@/src/lib/utils/date';
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

const EMPTY_BACKFILL = {
  participantName: '',
  occurredOn: today(),
  cash: 0,
  poolValuePreMoney: 0,
  writeMovementTransaction: false,
};

/**
 * Creates a pool over an existing account and seeds the owner.
 *
 * Two things this dialog refuses to guess:
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
              fields.map((field, index) => (
                <div
                  key={field.id}
                  className="space-y-2 rounded-md border bg-muted/20 p-3"
                  data-testid="pool-backfill-row"
                >
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
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <label className="inline-flex items-center gap-2 text-xs text-muted-foreground">
                      <input
                        type="checkbox"
                        data-testid={`backfill-write-tx-${index}`}
                        {...register(`backdatedSubscriptions.${index}.writeMovementTransaction`)}
                      />
                      <span>The arrival was never recorded on the account — write it now</span>
                    </label>
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
