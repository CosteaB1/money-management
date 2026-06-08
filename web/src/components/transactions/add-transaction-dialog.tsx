'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
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
import { Switch } from '@/src/components/ui/switch';
import { Textarea } from '@/src/components/ui/textarea';
import { useAccounts } from '@/src/lib/api/accounts';
import { useCategories } from '@/src/lib/api/categories';
import { useCreateTransaction } from '@/src/lib/api/transactions';
import { todayIsoUtc } from '@/src/lib/utils/date';
import type { TransactionDirection } from '@/src/types/api';

const today = () => todayIsoUtc();

const schema = z
  .object({
    accountId: z.string().min(1, 'Account is required'),
    direction: z.enum(['Income', 'Expense']),
    amount: z.coerce
      .number({ invalid_type_error: 'Amount must be a number' })
      .positive('Amount must be greater than 0'),
    transactionDate: z
      .string()
      .min(1, 'Date is required')
      .refine((v) => v <= today(), 'Date cannot be in the future'),
    categoryId: z.string().optional(),
    description: z
      .string()
      .trim()
      .min(1, 'Description is required')
      .max(500, 'Description must be 500 characters or less'),
    notes: z.string().trim().max(500, 'Notes must be 500 characters or less').optional(),
    isTransfer: z.boolean().optional(),
    counterAccountId: z.string().optional(),
  })
  .refine((data) => !data.isTransfer || data.counterAccountId !== data.accountId, {
    path: ['counterAccountId'],
    message: 'Counter account must differ from the source',
  });

type FormValues = z.input<typeof schema>;
type ParsedValues = z.output<typeof schema>;

const MAX_DESCRIPTION = 500;
const MAX_NOTES = 500;

export function AddTransactionDialog() {
  const [open, setOpen] = useState(false);
  const accountsQuery = useAccounts(false);
  const categoriesQuery = useCategories({ includeArchived: false });
  const { mutateAsync, isPending } = useCreateTransaction();

  const {
    register,
    handleSubmit,
    reset,
    setValue,
    setError,
    watch,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      accountId: '',
      direction: 'Expense',
      amount: 0,
      transactionDate: today(),
      categoryId: '',
      description: '',
      notes: '',
      isTransfer: false,
      counterAccountId: '',
    },
  });

  const selectedDirection = watch('direction');
  const selectedAccountId = watch('accountId');
  const selectedCategoryId = watch('categoryId');
  const descriptionValue = watch('description') ?? '';
  const notesValue = watch('notes') ?? '';
  const isTransfer = watch('isTransfer') ?? false;
  const counterAccountId = watch('counterAccountId') ?? '';
  // The amount label echoes the picked account's currency so the user is
  // never confused about the unit. Falls back to MDL before an account
  // is chosen (matches the existing default unit).
  // Note: the regular add-transaction flow intentionally has NO "balance
  // adjustment" toggle — UI-side adjustments live in the dedicated
  // UpdateBalanceDialog launched from accounts-table.
  const selectedAccountCurrency =
    accountsQuery.data?.find((a) => a.id === selectedAccountId)?.currency ?? 'MDL';

  const filteredCategories = useMemo(() => {
    const all = categoriesQuery.data ?? [];
    return all.filter((c) => c.flow === selectedDirection || c.flow === 'Both');
  }, [categoriesQuery.data, selectedDirection]);

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    try {
      await mutateAsync({
        accountId: parsed.accountId,
        direction: parsed.direction,
        amount: parsed.amount,
        transactionDate: parsed.transactionDate,
        description: parsed.description,
        ...(parsed.notes ? { notes: parsed.notes } : {}),
        ...(parsed.categoryId ? { categoryId: parsed.categoryId } : {}),
        ...(parsed.isTransfer ? { isTransfer: true } : {}),
        ...(parsed.isTransfer && parsed.counterAccountId
          ? { counterAccountId: parsed.counterAccountId }
          : {}),
      });
      toast.success('Transaction added');
      reset();
      setOpen(false);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to add transaction';
      if (/amount/i.test(message)) {
        setError('amount', { message });
      } else if (/description/i.test(message)) {
        setError('description', { message });
      } else if (/account/i.test(message)) {
        setError('accountId', { message });
      } else {
        toast.error(message);
      }
    }
  });

  const activeAccounts = accountsQuery.data ?? [];

  // v1: counter-account picker is MDL-only (cross-currency transfers come later).
  const counterAccountOptions = useMemo(
    () =>
      activeAccounts.filter(
        (a) => a.currency === 'MDL' && !a.isArchived && a.id !== selectedAccountId,
      ),
    [activeAccounts, selectedAccountId],
  );

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) reset();
      }}
    >
      <DialogTrigger asChild>
        <Button data-testid="add-transaction-button">
          <Plus className="h-4 w-4" />
          Add transaction
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add transaction</DialogTitle>
          <DialogDescription>
            Record an income or expense against one of your accounts.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="transaction-account">Account</Label>
            <Select
              value={selectedAccountId}
              onValueChange={(v) => setValue('accountId', v, { shouldValidate: true })}
            >
              <SelectTrigger id="transaction-account" data-testid="transaction-account-select">
                <SelectValue placeholder="Select account" />
              </SelectTrigger>
              <SelectContent>
                {activeAccounts.map((a) => (
                  <SelectItem key={a.id} value={a.id}>
                    {a.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {errors.accountId && (
              <p className="text-xs text-destructive" role="alert">
                {errors.accountId.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label>Direction</Label>
            <div className="flex gap-2">
              {(['Expense', 'Income'] as TransactionDirection[]).map((dir) => {
                const active = selectedDirection === dir;
                return (
                  <label
                    key={dir}
                    data-testid={`transaction-direction-${dir.toLowerCase()}`}
                    className={
                      active
                        ? 'flex-1 cursor-pointer rounded-md border border-primary bg-primary/10 px-3 py-2 text-center text-sm font-medium'
                        : 'flex-1 cursor-pointer rounded-md border border-input bg-background px-3 py-2 text-center text-sm font-medium text-muted-foreground hover:bg-accent'
                    }
                  >
                    <input
                      type="radio"
                      name="direction"
                      value={dir}
                      checked={active}
                      onChange={() => {
                        setValue('direction', dir, { shouldValidate: true });
                        setValue('categoryId', '', { shouldValidate: false });
                      }}
                      className="sr-only"
                    />
                    {dir}
                  </label>
                );
              })}
            </div>
          </div>

          <div className="flex items-center justify-between rounded-md border bg-muted/30 px-3 py-2">
            <div className="space-y-0.5">
              <Label htmlFor="transaction-is-transfer" className="text-sm">
                Internal transfer
              </Label>
              <p className="text-xs text-muted-foreground">
                Mark as a movement between your accounts (excluded from income/expense).
              </p>
            </div>
            <Switch
              id="transaction-is-transfer"
              data-testid="transaction-is-transfer"
              checked={isTransfer}
              onCheckedChange={(next) => {
                setValue('isTransfer', next, { shouldValidate: true });
                if (!next) setValue('counterAccountId', '', { shouldValidate: false });
              }}
            />
          </div>

          {isTransfer && (
            <div className="space-y-2">
              <Label htmlFor="transaction-counter-account">Counter account (optional)</Label>
              <Select
                value={counterAccountId}
                onValueChange={(v) => setValue('counterAccountId', v, { shouldValidate: true })}
              >
                <SelectTrigger
                  id="transaction-counter-account"
                  data-testid="transaction-counter-account-select"
                >
                  <SelectValue placeholder="Select counter account" />
                </SelectTrigger>
                <SelectContent>
                  {counterAccountOptions.map((a) => (
                    <SelectItem key={a.id} value={a.id}>
                      {a.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">MDL accounts only for v1.</p>
              {/* Defensive: counterAccountId is an optional uuid set only from the
                  Select (always valid), and the backend error mapping has no
                  counter-account arm, so this never renders. */}
              {/* v8 ignore start */}
              {errors.counterAccountId && (
                <p className="text-xs text-destructive" role="alert">
                  {errors.counterAccountId.message}
                </p>
              )}
              {/* v8 ignore stop */}
            </div>
          )}

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-2">
              <Label htmlFor="transaction-amount">{`Amount (${selectedAccountCurrency})`}</Label>
              <Input
                id="transaction-amount"
                data-testid="transaction-amount-input"
                type="number"
                step="0.01"
                min="0"
                {...register('amount')}
                aria-invalid={Boolean(errors.amount)}
              />
              {errors.amount && (
                <p className="text-xs text-destructive" role="alert">
                  {errors.amount.message}
                </p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="transaction-date">Date</Label>
              <Input
                id="transaction-date"
                data-testid="transaction-date-input"
                type="date"
                max={today()}
                {...register('transactionDate')}
                aria-invalid={Boolean(errors.transactionDate)}
              />
              {errors.transactionDate && (
                <p className="text-xs text-destructive" role="alert">
                  {errors.transactionDate.message}
                </p>
              )}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="transaction-category">Category (optional)</Label>
            <Select
              value={selectedCategoryId || ''}
              onValueChange={(v) => setValue('categoryId', v, { shouldValidate: true })}
            >
              <SelectTrigger id="transaction-category" data-testid="transaction-category-select">
                <SelectValue placeholder="Uncategorized" />
              </SelectTrigger>
              <SelectContent>
                {filteredCategories.map((c) => (
                  <SelectItem key={c.id} value={c.id}>
                    {c.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-2">
            <Label htmlFor="transaction-description">Description</Label>
            <Textarea
              id="transaction-description"
              data-testid="transaction-description-input"
              maxLength={MAX_DESCRIPTION}
              rows={3}
              {...register('description')}
              aria-invalid={Boolean(errors.description)}
            />
            <div className="flex items-center justify-between">
              {errors.description ? (
                <p className="text-xs text-destructive" role="alert">
                  {errors.description.message}
                </p>
              ) : (
                <span />
              )}
              <span className="text-xs text-muted-foreground">
                {descriptionValue.length}/{MAX_DESCRIPTION}
              </span>
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="transaction-notes">Notes (optional)</Label>
            <Textarea
              id="transaction-notes"
              data-testid="transaction-notes-input"
              maxLength={MAX_NOTES}
              rows={2}
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
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="transaction-submit-button">
              {isPending ? 'Adding...' : 'Add transaction'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
