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
import { useRecordLoanPayment } from '@/src/lib/api/loans';
import { formatMoney } from '@/src/lib/utils/currency';
import { todayIsoUtc } from '@/src/lib/utils/date';
import type { LoanDto } from '@/src/types/api';

const today = () => todayIsoUtc();

// Radix Select items can't carry an empty-string value — sentinel maps to
// `accountId: null` ("this repayment isn't tracked in any account").
const NO_ACCOUNT = 'none';

/**
 * Schema is built per-loan: the client-side max mirrors the loan's live
 * outstanding balance (the server re-validates), and the payment date is
 * clamped to [loanDate, today].
 */
function makeSchema(loan: LoanDto) {
  return z.object({
    amount: z.coerce
      .number({ invalid_type_error: 'Amount must be a number' })
      .positive('Amount must be greater than 0')
      .max(
        loan.outstanding,
        `Amount cannot exceed the outstanding ${formatMoney(loan.outstanding, loan.currency)}`,
      ),
    occurredOn: z
      .string()
      .regex(/^\d{4}-\d{2}-\d{2}$/, 'Payment date is required')
      .refine((v) => v <= today(), { message: 'Payment date cannot be in the future' })
      .refine((v) => v >= loan.loanDate, {
        message: 'Payment date cannot be before the loan date',
      }),
    accountId: z.string().uuid('Pick a valid account').optional(),
    notes: z.string().trim().max(500, 'Notes must be 500 characters or less').optional(),
  });
}

type FormValues = z.input<ReturnType<typeof makeSchema>>;
type ParsedValues = z.output<ReturnType<typeof makeSchema>>;

interface Props {
  loan: LoanDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Records a partial (or full) repayment against a loan. Shared by the
 * loans-table row action and the detail-page header. Optionally links the
 * repayment to an account — the backend then writes a transfer-flagged
 * transaction on it, so the movement never pollutes income/expense stats.
 */
export function RecordPaymentDialog({ loan, open, onOpenChange }: Props) {
  const [apiError, setApiError] = useState<string | null>(null);
  const accountsQuery = useAccounts(false);
  const { mutateAsync, isPending } = useRecordLoanPayment(loan.id);

  // v1 is same-currency only — offer only non-archived accounts in the
  // loan's currency.
  const matchingAccounts =
    accountsQuery.data?.filter((a) => !a.isArchived && a.currency === loan.currency) ?? [];

  const {
    handleSubmit,
    register,
    reset,
    setValue,
    watch,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(makeSchema(loan)),
    defaultValues: {
      amount: 0,
      occurredOn: today(),
      accountId: undefined,
      notes: '',
    },
  });

  const accountId = watch('accountId');

  const contextLine =
    loan.direction === 'Received'
      ? `Repaying loan from ${loan.counterparty} — outstanding ${formatMoney(loan.outstanding, loan.currency)}`
      : `Recording repayment from ${loan.counterparty} — outstanding ${formatMoney(loan.outstanding, loan.currency)}`;

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    setApiError(null);
    try {
      await mutateAsync({
        amount: parsed.amount,
        occurredOn: parsed.occurredOn,
        accountId: parsed.accountId ?? null,
        notes: parsed.notes && parsed.notes.length > 0 ? parsed.notes : null,
      });
      toast.success('Payment recorded');
      reset();
      onOpenChange(false);
    } catch (err) {
      if (err instanceof ApiError) {
        setApiError(err.message);
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to record payment');
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
      <DialogContent data-testid="record-payment-dialog">
        <DialogHeader>
          <DialogTitle>Record payment</DialogTitle>
          <DialogDescription data-testid="record-payment-context">{contextLine}</DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-2">
              <Label htmlFor="payment-amount">Amount ({loan.currency})</Label>
              <Input
                id="payment-amount"
                data-testid="payment-amount-input"
                type="number"
                step="0.01"
                min="0"
                max={loan.outstanding}
                {...register('amount')}
                aria-invalid={Boolean(errors.amount)}
                aria-describedby={errors.amount ? 'payment-amount-error' : undefined}
              />
              {errors.amount && (
                <p id="payment-amount-error" className="text-xs text-destructive" role="alert">
                  {errors.amount.message}
                </p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="payment-date">Date</Label>
              <Input
                id="payment-date"
                data-testid="payment-date-input"
                type="date"
                min={loan.loanDate}
                max={today()}
                {...register('occurredOn')}
                aria-invalid={Boolean(errors.occurredOn)}
                aria-describedby={errors.occurredOn ? 'payment-date-error' : undefined}
              />
              {errors.occurredOn && (
                <p id="payment-date-error" className="text-xs text-destructive" role="alert">
                  {errors.occurredOn.message}
                </p>
              )}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="payment-account">Account (optional)</Label>
            <Select
              value={accountId ?? NO_ACCOUNT}
              onValueChange={(v) =>
                setValue('accountId', v === NO_ACCOUNT ? undefined : v, { shouldValidate: true })
              }
            >
              <SelectTrigger id="payment-account" data-testid="payment-account-select">
                <SelectValue placeholder="No account" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NO_ACCOUNT} data-testid="payment-account-option-none">
                  No account
                </SelectItem>
                {matchingAccounts.map((a) => (
                  <SelectItem
                    key={a.id}
                    value={a.id}
                    data-testid={`payment-account-option-${a.name}`}
                  >
                    {a.name}
                  </SelectItem>
                ))}
                {matchingAccounts.length === 0 && (
                  <div className="px-2 py-1.5 text-sm text-muted-foreground">
                    No {loan.currency} accounts available.
                  </div>
                )}
              </SelectContent>
            </Select>
            <p className="text-xs text-muted-foreground">
              Picking an account records the money movement on it as a transaction.
            </p>
            {/* Defensive: the Select only emits seeded account UUIDs or the
                sentinel, so the uuid() validation never fails here. */}
            {/* v8 ignore start */}
            {errors.accountId && (
              <p className="text-xs text-destructive" role="alert">
                {errors.accountId.message}
              </p>
            )}
            {/* v8 ignore stop */}
          </div>

          <div className="space-y-2">
            <Label htmlFor="payment-notes">Notes (optional)</Label>
            <Textarea
              id="payment-notes"
              data-testid="payment-notes-input"
              maxLength={500}
              {...register('notes')}
              aria-invalid={Boolean(errors.notes)}
              aria-describedby={errors.notes ? 'payment-notes-error' : undefined}
            />
            {errors.notes && (
              <p id="payment-notes-error" className="text-xs text-destructive" role="alert">
                {errors.notes.message}
              </p>
            )}
          </div>

          {apiError && (
            <p className="text-sm text-destructive" role="alert" data-testid="record-payment-error">
              {apiError}
            </p>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="payment-submit-button">
              {isPending ? 'Recording...' : 'Record payment'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
