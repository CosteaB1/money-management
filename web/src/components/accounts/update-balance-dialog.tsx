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
import { useAdjustBalance } from '@/src/lib/api/accounts';
import { cn } from '@/src/lib/utils/cn';
import { formatMoney } from '@/src/lib/utils/currency';
import { todayIsoUtc } from '@/src/lib/utils/date';
import type { AccountDto, BalanceChangeKind } from '@/src/types/api';

const today = () => todayIsoUtc();
const MAX_NOTES = 500;

const KIND_OPTIONS: ReadonlyArray<{
  kind: BalanceChangeKind;
  label: string;
  testId: string;
}> = [
  { kind: 'Investment', label: 'Investment', testId: 'balance-kind-investment' },
  { kind: 'Withdrawal', label: 'Withdrawal', testId: 'balance-kind-withdrawal' },
  { kind: 'Adjustment', label: 'Balance adjustment', testId: 'balance-kind-adjustment' },
];

const schema = z.object({
  // Pre-parse so empty/whitespace inputs become NaN (caught below) instead of
  // being silently coerced to 0 by `z.coerce.number()`. The user must
  // explicitly enter a value — even when (for Adjustment) it equals the
  // current balance, the backend rejects the 0-delta and we surface that.
  value: z.preprocess(
    (v) => (typeof v === 'string' && v.trim() === '' ? Number.NaN : Number(v)),
    z
      .number({ invalid_type_error: 'Value must be a number' })
      .refine((n) => !Number.isNaN(n), { message: 'Value must be a number' }),
  ),
  date: z
    .string()
    .min(1, 'Date is required')
    .refine((v) => v <= today(), 'Date cannot be in the future'),
  notes: z
    .string()
    .trim()
    .max(MAX_NOTES, `Notes must be ${MAX_NOTES} characters or less`)
    .optional()
    .or(z.literal('')),
});

type FormValues = z.input<typeof schema>;
type ParsedValues = z.output<typeof schema>;

interface Props {
  account: AccountDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Records a balance change against an account whose balance moves
 * independently of explicit transactions (Brokerage, CryptoExchange,
 * P2PLending, BankDeposit). Three modes share the dialog:
 *
 *   - **Investment** — money moved INTO the account (`value` = amount, > 0).
 *   - **Withdrawal** — money moved OUT of the account (`value` = amount, > 0).
 *   - **Balance adjustment** — set the NEW TOTAL balance (`value` = new
 *     total); the backend writes a single income-or-expense leg for the
 *     P&L delta `value - currentBalance`.
 *
 * The "current balance" preview (Adjustment mode) comes straight from
 * `account.balance`, which the backend computes live as
 * `anchor + Σ income − Σ expense` across all non-deleted transactions.
 */
export function UpdateBalanceDialog({ account, open, onOpenChange }: Props) {
  const { mutateAsync, isPending } = useAdjustBalance(account.id);
  const currentBalance = account.balance;
  const [kind, setKind] = useState<BalanceChangeKind>('Investment');

  const {
    register,
    handleSubmit,
    reset,
    setError,
    watch,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      // Adjustment seeds with the current balance (the "set new total" UX);
      // Investment/Withdrawal start blank so the user types an amount.
      value: '',
      date: today(),
      notes: '',
    },
  });

  const notesValue = watch('notes') ?? '';
  const isAdjustment = kind === 'Adjustment';
  const valueLabel = isAdjustment
    ? `New balance (${account.currency})`
    : `Amount (${account.currency})`;

  const resetAll = () => {
    reset();
    setKind('Investment');
  };

  const switchKind = (next: BalanceChangeKind) => {
    setKind(next);
    // Seed the field for the new mode: prefill the current balance for
    // Adjustment, clear it for Investment/Withdrawal. Clearing the error too
    // so a leftover validation from the previous mode doesn't linger.
    reset({
      value: next === 'Adjustment' ? currentBalance : '',
      date: watch('date'),
      notes: watch('notes'),
    });
  };

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;

    // Amount-mode guard: Investment/Withdrawal require a strictly positive
    // value. (Adjustment allows any number; the 0-delta case is caught by the
    // backend and surfaced inline below.)
    if (!isAdjustment && !(parsed.value > 0)) {
      setError('value', { message: 'Amount must be greater than 0' });
      return;
    }

    try {
      const result = await mutateAsync({
        kind,
        value: parsed.value,
        date: parsed.date,
        ...(parsed.notes ? { notes: parsed.notes } : {}),
      });

      if (kind === 'Investment') {
        toast.success(`Recorded investment of ${formatMoney(parsed.value, account.currency)}`);
      } else if (kind === 'Withdrawal') {
        toast.success(`Recorded withdrawal of ${formatMoney(parsed.value, account.currency)}`);
      } else {
        const direction = result.delta >= 0 ? 'Increased' : 'Decreased';
        const magnitude = formatMoney(Math.abs(result.delta), account.currency);
        toast.success(`${direction} by ${magnitude}`);
      }

      resetAll();
      onOpenChange(false);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to record balance change';
      // 0-delta is the most likely Adjustment failure — surface it inline on
      // the value field so the user knows the new balance needs to change.
      if (isAdjustment && /no change|delta|same/i.test(message)) {
        setError('value', { message });
      } else {
        toast.error(message);
      }
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next);
        if (!next) resetAll();
      }}
    >
      <DialogContent data-testid="update-balance-dialog">
        <DialogHeader>
          <DialogTitle>Update balance</DialogTitle>
          <DialogDescription>
            Record a money-in (<strong>investment</strong>), money-out (<strong>withdrawal</strong>
            ), or set a new total (<strong>balance adjustment</strong>) on{' '}
            <strong>{account.name}</strong>. The backend writes a single synthetic income or expense
            for the resulting change.
          </DialogDescription>
        </DialogHeader>

        <fieldset className="space-y-2">
          <legend className="text-sm font-medium leading-none">Action</legend>
          <div
            id="balance-change-kind"
            data-testid="balance-change-kind"
            className="grid grid-cols-3 gap-2"
          >
            {KIND_OPTIONS.map((option) => {
              const active = kind === option.kind;
              return (
                <Button
                  key={option.kind}
                  type="button"
                  variant={active ? 'default' : 'outline'}
                  size="sm"
                  aria-pressed={active}
                  data-testid={option.testId}
                  onClick={() => switchKind(option.kind)}
                  className={cn('h-auto whitespace-normal py-2 text-xs')}
                >
                  {option.label}
                </Button>
              );
            })}
          </div>
        </fieldset>

        {isAdjustment && (
          <div
            className="rounded-md border border-dashed bg-muted/30 px-3 py-2 text-xs text-muted-foreground"
            data-testid="update-balance-current"
          >
            <span>
              Current balance:{' '}
              <span className="font-medium text-foreground tabular-nums">
                {formatMoney(currentBalance, account.currency)}
              </span>
            </span>
          </div>
        )}

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="balance-value">{valueLabel}</Label>
            <Input
              id="balance-value"
              // Distinct testids per mode so suites can target the field by
              // its semantic: the amount field for Investment/Withdrawal vs
              // the new-balance field for Adjustment.
              data-testid={isAdjustment ? 'adjust-new-balance-input' : 'balance-amount-input'}
              type="number"
              step="0.01"
              {...(isAdjustment ? {} : { min: '0' })}
              {...register('value')}
              aria-invalid={Boolean(errors.value)}
            />
            {errors.value && (
              <p className="text-xs text-destructive" role="alert">
                {errors.value.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="adjust-date">Date</Label>
            <Input
              id="adjust-date"
              data-testid="adjust-date-input"
              type="date"
              max={today()}
              {...register('date')}
              aria-invalid={Boolean(errors.date)}
            />
            {errors.date && (
              <p className="text-xs text-destructive" role="alert">
                {errors.date.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="adjust-notes">Notes (optional)</Label>
            <Textarea
              id="adjust-notes"
              data-testid="adjust-notes-input"
              maxLength={MAX_NOTES}
              rows={3}
              {...register('notes')}
              aria-invalid={Boolean(errors.notes)}
            />
            <div className="flex items-center justify-between">
              {errors.notes ? (
                <p className="text-xs text-destructive" role="alert">
                  {errors.notes.message}
                </p>
              ) : (
                <span />
              )}
              <span className="text-xs text-muted-foreground">
                {notesValue.length}/{MAX_NOTES}
              </span>
            </div>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="adjust-submit-button">
              {isPending ? 'Recording...' : 'Record change'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
