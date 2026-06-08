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
import { useCreateAccount } from '@/src/lib/api/accounts';
import { toIsoDateString } from '@/src/lib/utils/date';
import type { AccountType } from '@/src/types/api';

const ACCOUNT_TYPE_OPTIONS = [
  { value: 'Cash', label: 'Cash' },
  { value: 'CreditCard', label: 'Credit card' },
  { value: 'BankDeposit', label: 'Bank deposit' },
  { value: 'BankCurrent', label: 'Bank current' },
  { value: 'Brokerage', label: 'Brokerage' },
  { value: 'CryptoExchange', label: 'Crypto exchange' },
  { value: 'P2PLending', label: 'P2P lending' },
] as const;

const CURRENCY_OPTIONS = ['MDL', 'USD', 'EUR', 'RON', 'GBP'] as const;

const schema = z
  .object({
    name: z.string().min(1, 'Name is required').max(100),
    type: z.enum([
      'Cash',
      'CreditCard',
      'BankDeposit',
      'BankCurrent',
      'Brokerage',
      'CryptoExchange',
      'P2PLending',
    ]),
    currency: z.string().regex(/^[A-Z]{3}$/, 'Currency must be a 3-letter ISO code (e.g. MDL)'),
    balance: z.coerce.number({
      invalid_type_error: 'Balance must be a number',
    }),
    openingDate: z.coerce.date(),
    notes: z.string().max(1000).optional(),
  })
  .refine((d) => d.balance >= 0 || d.type === 'CreditCard', {
    path: ['balance'],
    message: 'Only credit cards can have a negative balance',
  });

type FormValues = z.input<typeof schema>;
type ParsedValues = z.output<typeof schema>;

export function CreateAccountDialog() {
  const [open, setOpen] = useState(false);
  const { mutateAsync, isPending } = useCreateAccount();

  const {
    register,
    handleSubmit,
    reset,
    setValue,
    watch,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: '',
      type: 'Cash',
      currency: 'MDL',
      balance: 0,
      openingDate: toIsoDateString(new Date()) as unknown as Date,
      notes: '',
    },
  });

  const selectedType = watch('type');
  const selectedCurrency = watch('currency');

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    try {
      await mutateAsync({
        name: parsed.name,
        type: parsed.type,
        currency: parsed.currency,
        balance: parsed.balance,
        openingDate: toIsoDateString(parsed.openingDate),
        notes: parsed.notes && parsed.notes.length > 0 ? parsed.notes : null,
      });
      toast.success('Account created');
      reset();
      setOpen(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to create account');
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) reset();
      }}
    >
      <DialogTrigger asChild>
        <Button data-testid="add-account-button">
          <Plus className="h-4 w-4" />
          Add account
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add account</DialogTitle>
          <DialogDescription>Create a new account.</DialogDescription>
        </DialogHeader>
        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="account-name">Name</Label>
            <Input
              id="account-name"
              data-testid="account-name-input"
              {...register('name')}
              aria-invalid={Boolean(errors.name)}
              aria-describedby={errors.name ? 'account-name-error' : undefined}
            />
            {errors.name && (
              <p id="account-name-error" className="text-xs text-destructive" role="alert">
                {errors.name.message}
              </p>
            )}
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-2">
              <Label htmlFor="account-type">Type</Label>
              <Select
                value={selectedType}
                onValueChange={(v) => setValue('type', v as AccountType, { shouldValidate: true })}
              >
                <SelectTrigger id="account-type" data-testid="account-type-select">
                  <SelectValue placeholder="Select type" />
                </SelectTrigger>
                <SelectContent>
                  {ACCOUNT_TYPE_OPTIONS.map((option) => (
                    <SelectItem key={option.value} value={option.value}>
                      {option.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-2">
              <Label htmlFor="account-currency">Currency</Label>
              <Select
                value={selectedCurrency}
                onValueChange={(v) => setValue('currency', v, { shouldValidate: true })}
              >
                <SelectTrigger id="account-currency" data-testid="account-currency-select">
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

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-2">
              <Label htmlFor="account-balance">Balance</Label>
              <Input
                id="account-balance"
                data-testid="account-balance-input"
                type="number"
                step="0.01"
                {...register('balance')}
                aria-invalid={Boolean(errors.balance)}
                aria-describedby={errors.balance ? 'account-balance-error' : 'account-balance-hint'}
              />
              {errors.balance ? (
                <p id="account-balance-error" className="text-xs text-destructive" role="alert">
                  {errors.balance.message}
                </p>
              ) : (
                <p id="account-balance-hint" className="text-xs text-muted-foreground">
                  Set at account creation; later changes happen via transactions or balance
                  adjustments.
                </p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="account-opening-date">Opening date</Label>
              <Input
                id="account-opening-date"
                data-testid="account-opening-date-input"
                type="date"
                {...register('openingDate')}
                aria-invalid={Boolean(errors.openingDate)}
              />
              {errors.openingDate && (
                <p className="text-xs text-destructive" role="alert">
                  {errors.openingDate.message}
                </p>
              )}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="account-notes">Notes (optional)</Label>
            <Input id="account-notes" {...register('notes')} />
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="account-submit-button">
              {isPending ? 'Creating...' : 'Create account'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
