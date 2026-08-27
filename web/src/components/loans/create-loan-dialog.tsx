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
import { Textarea } from '@/src/components/ui/textarea';
import { useAccounts } from '@/src/lib/api/accounts';
import { ApiError } from '@/src/lib/api/client';
import { useCreateLoan } from '@/src/lib/api/loans';
import { todayIsoUtc } from '@/src/lib/utils/date';

const today = () => todayIsoUtc();

// Same currency list the create-account dialog offers.
const CURRENCY_OPTIONS = ['MDL', 'USD', 'EUR', 'RON', 'GBP'] as const;

// Radix Select items can't carry an empty-string value, so the explicit
// "No account" default rides on a sentinel that maps to `accountId: null`
// in the payload ("this money isn't tracked in any account").
const NO_ACCOUNT = 'none';

const schema = z.object({
  direction: z.enum(['Received', 'Given']),
  counterparty: z
    .string()
    .trim()
    .min(1, 'Counterparty is required')
    .max(100, 'Counterparty must be 100 characters or less'),
  principal: z.coerce
    .number({ invalid_type_error: 'Principal must be a number' })
    .positive('Principal must be greater than 0'),
  currency: z.string().regex(/^[A-Z]{3}$/, 'Currency must be a 3-letter ISO code (e.g. MDL)'),
  loanDate: z
    .string()
    .regex(/^\d{4}-\d{2}-\d{2}$/, 'Loan date is required')
    .refine((v) => v <= today(), { message: 'Loan date cannot be in the future' }),
  accountId: z.string().uuid('Pick a valid account').optional(),
  notes: z.string().trim().max(500, 'Notes must be 500 characters or less').optional(),
});

type FormValues = z.input<typeof schema>;
type ParsedValues = z.output<typeof schema>;

export function CreateLoanDialog() {
  const [open, setOpen] = useState(false);
  const [apiError, setApiError] = useState<string | null>(null);
  const accountsQuery = useAccounts(false);
  const { mutateAsync, isPending } = useCreateLoan();

  const accounts = accountsQuery.data?.filter((a) => !a.isArchived) ?? [];

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
      direction: 'Received',
      counterparty: '',
      principal: 0,
      currency: 'MDL',
      loanDate: today(),
      accountId: undefined,
      notes: '',
    },
  });

  const direction = watch('direction');
  const currency = watch('currency');
  const accountId = watch('accountId');

  // v1 is same-currency only: the account picker offers only non-archived
  // accounts whose currency matches the loan currency.
  const matchingAccounts = accounts.filter((a) => a.currency === currency);

  const handleCurrencyChange = (next: string) => {
    setValue('currency', next, { shouldValidate: true });
    // Drop an account selection that no longer matches the loan currency.
    const selected = accounts.find((a) => a.id === accountId);
    if (selected && selected.currency !== next) {
      setValue('accountId', undefined, { shouldValidate: false });
    }
  };

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    setApiError(null);
    try {
      await mutateAsync({
        direction: parsed.direction,
        counterparty: parsed.counterparty,
        principal: parsed.principal,
        currency: parsed.currency,
        loanDate: parsed.loanDate,
        accountId: parsed.accountId ?? null,
        notes: parsed.notes && parsed.notes.length > 0 ? parsed.notes : null,
      });
      toast.success('Loan created');
      reset();
      setOpen(false);
    } catch (err) {
      if (err instanceof ApiError) {
        setApiError(err.message);
        return;
      }
      toast.error(err instanceof Error ? err.message : 'Failed to create loan');
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) {
          reset();
          setApiError(null);
        }
      }}
    >
      <DialogTrigger asChild>
        <Button data-testid="add-loan-button">
          <Plus className="h-4 w-4" />
          Add loan
        </Button>
      </DialogTrigger>
      <DialogContent data-testid="create-loan-dialog">
        <DialogHeader>
          <DialogTitle>Add loan</DialogTitle>
          <DialogDescription>
            Track money you borrowed or lent — no interest, repaid over time in the same currency.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <fieldset className="space-y-2" data-testid="loan-direction-fieldset">
            <legend className="text-sm font-medium">Direction</legend>
            <div className="flex flex-col gap-2 rounded-md border bg-muted/20 p-3 sm:flex-row sm:gap-4">
              <label className="inline-flex items-center gap-2 text-sm">
                <input
                  type="radio"
                  value="Received"
                  checked={direction === 'Received'}
                  onChange={() => setValue('direction', 'Received', { shouldValidate: true })}
                  data-testid="loan-direction-received"
                />
                <span>I borrowed money</span>
              </label>
              <label className="inline-flex items-center gap-2 text-sm">
                <input
                  type="radio"
                  value="Given"
                  checked={direction === 'Given'}
                  onChange={() => setValue('direction', 'Given', { shouldValidate: true })}
                  data-testid="loan-direction-given"
                />
                <span>I lent money</span>
              </label>
            </div>
            <p className="text-xs text-muted-foreground">
              {direction === 'Received'
                ? 'Money you borrowed — you owe it back.'
                : 'Money you lent out — it is owed back to you.'}
            </p>
          </fieldset>

          <div className="space-y-2">
            <Label htmlFor="loan-counterparty">Counterparty</Label>
            <Input
              id="loan-counterparty"
              data-testid="loan-counterparty-input"
              maxLength={100}
              placeholder="e.g. Parents"
              {...register('counterparty')}
              aria-invalid={Boolean(errors.counterparty)}
              aria-describedby={errors.counterparty ? 'loan-counterparty-error' : undefined}
            />
            {errors.counterparty && (
              <p id="loan-counterparty-error" className="text-xs text-destructive" role="alert">
                {errors.counterparty.message}
              </p>
            )}
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-2">
              <Label htmlFor="loan-principal">Principal ({currency})</Label>
              <Input
                id="loan-principal"
                data-testid="loan-principal-input"
                type="number"
                step="0.01"
                min="0"
                {...register('principal')}
                aria-invalid={Boolean(errors.principal)}
                aria-describedby={errors.principal ? 'loan-principal-error' : undefined}
              />
              {errors.principal && (
                <p id="loan-principal-error" className="text-xs text-destructive" role="alert">
                  {errors.principal.message}
                </p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="loan-currency">Currency</Label>
              <Select value={currency} onValueChange={handleCurrencyChange}>
                <SelectTrigger id="loan-currency" data-testid="loan-currency-select">
                  <SelectValue placeholder="Select currency" />
                </SelectTrigger>
                <SelectContent>
                  {CURRENCY_OPTIONS.map((code) => (
                    <SelectItem key={code} value={code}>
                      {code}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {/* Defensive: the currency Select only emits valid 3-letter ISO
                  codes, so the Zod regex never fails here. */}
              {/* v8 ignore start */}
              {errors.currency && (
                <p className="text-xs text-destructive" role="alert">
                  {errors.currency.message}
                </p>
              )}
              {/* v8 ignore stop */}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="loan-date">Loan date</Label>
            <Input
              id="loan-date"
              data-testid="loan-date-input"
              type="date"
              max={today()}
              {...register('loanDate')}
              aria-invalid={Boolean(errors.loanDate)}
              aria-describedby={errors.loanDate ? 'loan-date-error' : undefined}
            />
            {errors.loanDate && (
              <p id="loan-date-error" className="text-xs text-destructive" role="alert">
                {errors.loanDate.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="loan-account">Account (optional)</Label>
            <Select
              value={accountId ?? NO_ACCOUNT}
              onValueChange={(v) =>
                setValue('accountId', v === NO_ACCOUNT ? undefined : v, { shouldValidate: true })
              }
            >
              <SelectTrigger id="loan-account" data-testid="loan-account-select">
                <SelectValue placeholder="No account" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NO_ACCOUNT} data-testid="loan-account-option-none">
                  No account
                </SelectItem>
                {matchingAccounts.map((a) => (
                  <SelectItem key={a.id} value={a.id} data-testid={`loan-account-option-${a.name}`}>
                    {a.name}
                  </SelectItem>
                ))}
                {matchingAccounts.length === 0 && (
                  <div className="px-2 py-1.5 text-sm text-muted-foreground">
                    No {currency} accounts available.
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
            <Label htmlFor="loan-notes">Notes (optional)</Label>
            <Textarea
              id="loan-notes"
              data-testid="loan-notes-input"
              maxLength={500}
              {...register('notes')}
              aria-invalid={Boolean(errors.notes)}
              aria-describedby={errors.notes ? 'loan-notes-error' : undefined}
            />
            {errors.notes && (
              <p id="loan-notes-error" className="text-xs text-destructive" role="alert">
                {errors.notes.message}
              </p>
            )}
          </div>

          {apiError && (
            <p className="text-sm text-destructive" role="alert" data-testid="create-loan-error">
              {apiError}
            </p>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="loan-submit-button">
              {isPending ? 'Creating...' : 'Create loan'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
