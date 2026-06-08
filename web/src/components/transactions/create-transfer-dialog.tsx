'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { ArrowLeftRight } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
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
import { useCategories } from '@/src/lib/api/categories';
import { convertFx } from '@/src/lib/api/fx-rates';
import { useCreateTransfer } from '@/src/lib/api/transactions';
import { formatEffectiveRate } from '@/src/lib/utils/currency';
import { todayIsoUtc } from '@/src/lib/utils/date';

const today = () => todayIsoUtc();

const MAX_DESCRIPTION = 500;
const MAX_NOTES = 500;

const schema = z
  .object({
    sourceAccountId: z.string().min(1, 'Source account is required'),
    destinationAccountId: z.string().min(1, 'Destination account is required'),
    amount: z.coerce
      .number({ invalid_type_error: 'Amount must be a number' })
      .positive('Amount must be greater than 0'),
    date: z
      .string()
      .min(1, 'Date is required')
      .refine((v) => v <= today(), 'Date cannot be in the future'),
    description: z
      .string()
      .trim()
      .min(1, 'Description is required')
      .max(MAX_DESCRIPTION, `Description must be ${MAX_DESCRIPTION} characters or less`),
    categoryId: z.string().optional(),
    notes: z.string().trim().max(500, 'Notes must be 500 characters or less').optional(),
  })
  .refine((data) => data.sourceAccountId !== data.destinationAccountId, {
    path: ['destinationAccountId'],
    message: 'Destination must be different from source',
  });

type FormValues = z.input<typeof schema>;
type ParsedValues = z.output<typeof schema>;

/**
 * Optional props for the dialog. None of them are required — the original
 * call-sites (e.g. transactions page) keep working unchanged.
 *
 *   - `open` / `onOpenChange` — when supplied, the dialog becomes a
 *     controlled component and the built-in trigger button is hidden so
 *     the caller can drive open-state from elsewhere (e.g. the account
 *     detail page's action menu opening the dialog with a preselected
 *     source or destination).
 *   - `defaultSourceAccountId` / `defaultDestinationAccountId` — initial
 *     values for the source / destination selects. They flow into the
 *     form on open and reset back to the empty default when the dialog
 *     closes. Caller is responsible for ensuring the supplied account is
 *     transfer-eligible (any non-archived account); the select will silently
 *     ignore ineligible ids since they won't appear in the options list.
 *   - `triggerLabel` — replaces the default "New transfer" button label
 *     for uncontrolled usage. Ignored when `open` is supplied.
 */
interface CreateTransferDialogProps {
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  defaultSourceAccountId?: string;
  defaultDestinationAccountId?: string;
  triggerLabel?: string;
}

export function CreateTransferDialog({
  open: openProp,
  onOpenChange,
  defaultSourceAccountId,
  defaultDestinationAccountId,
  triggerLabel,
}: CreateTransferDialogProps = {}) {
  const isControlled = openProp !== undefined;
  const [internalOpen, setInternalOpen] = useState(false);
  const open = isControlled ? openProp : internalOpen;
  const setOpen = (next: boolean) => {
    if (!isControlled) setInternalOpen(next);
    onOpenChange?.(next);
  };
  const accountsQuery = useAccounts(false);
  const categoriesQuery = useCategories({ includeArchived: false });
  const { mutateAsync, isPending } = useCreateTransfer();

  // Any non-archived account is transfer-eligible on either side. When the
  // source and destination currencies differ, the dialog reveals a
  // destination-amount field (see `crossCurrency` below).
  const eligibleAccounts = useMemo(
    () => (accountsQuery.data ?? []).filter((a) => !a.isArchived),
    [accountsQuery.data],
  );

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
      sourceAccountId: defaultSourceAccountId ?? '',
      destinationAccountId: defaultDestinationAccountId ?? '',
      amount: 0,
      date: today(),
      description: '',
      categoryId: '',
      notes: '',
    },
  });

  const sourceId = watch('sourceAccountId');
  const destinationId = watch('destinationAccountId');
  const categoryId = watch('categoryId');
  const descriptionValue = watch('description') ?? '';
  const notesValue = watch('notes') ?? '';
  const amountValue = watch('amount');
  const dateValue = watch('date');

  // Cross-currency destination amount lives outside react-hook-form's schema
  // because the zod refine can't see the selected accounts' currencies. We
  // track the raw input string + a "dirty" flag so an FX pre-fill never
  // clobbers a value the user typed.
  const [destinationAmount, setDestinationAmount] = useState('');
  const [destinationAmountDirty, setDestinationAmountDirty] = useState(false);
  const [destinationAmountError, setDestinationAmountError] = useState<string | null>(null);

  const sourceAccount = useMemo(
    () => eligibleAccounts.find((a) => a.id === sourceId),
    [eligibleAccounts, sourceId],
  );
  const destinationAccount = useMemo(
    () => eligibleAccounts.find((a) => a.id === destinationId),
    [eligibleAccounts, destinationId],
  );
  const sourceCurrency = sourceAccount?.currency ?? 'MDL';
  const destinationCurrency = destinationAccount?.currency;
  const crossCurrency = Boolean(
    sourceAccount && destinationAccount && sourceAccount.currency !== destinationAccount.currency,
  );

  // Live effective rate from what the user actually entered — source/dest
  // amounts, NOT the convert endpoint's pre-fill (per the spec, the endpoint
  // only seeds the field). Recomputes on every keystroke.
  const numericDestination = Number(destinationAmount);
  const numericSource = Number(amountValue);
  const rateLabel =
    crossCurrency && destinationCurrency
      ? formatEffectiveRate(numericSource, numericDestination, sourceCurrency, destinationCurrency)
      : null;

  // When the dialog opens (either via the built-in trigger or controlled
  // open-state), reseat the preselected source/destination so callers can
  // open the same instance with different defaults on subsequent opens.
  // react-hook-form's `defaultValues` only applies on mount — the explicit
  // setValue keeps the source of truth in sync.
  useEffect(() => {
    if (!open) return;
    if (defaultSourceAccountId !== undefined) {
      setValue('sourceAccountId', defaultSourceAccountId, { shouldValidate: false });
    }
    if (defaultDestinationAccountId !== undefined) {
      setValue('destinationAccountId', defaultDestinationAccountId, { shouldValidate: false });
    }
  }, [open, defaultSourceAccountId, defaultDestinationAccountId, setValue]);

  // When the field stops being relevant (same-currency or a side cleared),
  // wipe the destination amount + its dirty/error state so a later
  // cross-currency selection starts clean.
  useEffect(() => {
    if (!crossCurrency) {
      setDestinationAmount('');
      setDestinationAmountDirty(false);
      setDestinationAmountError(null);
    }
  }, [crossCurrency]);

  // FX pre-fill: when the destination field first becomes relevant (cross-
  // currency, both accounts + a positive source amount + a date), fetch the
  // converted amount and seed the field — unless the user already edited it.
  // Keyed on the inputs so changing currencies/amount/date re-seeds; a guard
  // ref prevents duplicate in-flight requests for the same key.
  const lastPrefillKey = useRef<string | null>(null);
  useEffect(() => {
    if (!open) return;
    if (!crossCurrency || !destinationCurrency) return;
    if (destinationAmountDirty) return;
    const amount = Number(amountValue);
    if (!Number.isFinite(amount) || amount <= 0) return;
    if (!dateValue) return;

    const key = `${sourceCurrency}|${destinationCurrency}|${dateValue}|${amount}`;
    if (lastPrefillKey.current === key) return;
    lastPrefillKey.current = key;

    let cancelled = false;
    convertFx({ from: sourceCurrency, to: destinationCurrency, date: dateValue, amount })
      .then((res) => {
        if (cancelled) return;
        // Only seed when the user still hasn't touched the field. `hasRate`
        // false ⇒ leave blank for the user to type.
        if (res.hasRate && res.convertedAmount !== null) {
          setDestinationAmount(res.convertedAmount.toFixed(2));
        }
      })
      .catch(() => {
        // A failed lookup just leaves the field blank — the user can type the
        // received amount manually. Reset the key so a retry can re-fire.
        // (Timing-flaky in jsdom: the effect cleanup usually flips `cancelled`
        // before this microtask runs.)
        /* v8 ignore start */
        if (!cancelled) lastPrefillKey.current = null;
        /* v8 ignore stop */
      });
    return () => {
      cancelled = true;
    };
  }, [
    open,
    crossCurrency,
    destinationCurrency,
    sourceCurrency,
    amountValue,
    dateValue,
    destinationAmountDirty,
  ]);

  const transferCategories = useMemo(
    () => (categoriesQuery.data ?? []).filter((c) => c.flow === 'Both'),
    [categoriesQuery.data],
  );

  const resetForm = () => {
    reset();
    setDestinationAmount('');
    setDestinationAmountDirty(false);
    setDestinationAmountError(null);
    lastPrefillKey.current = null;
  };

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;

    // The zod schema can't see the selected accounts' currencies, so the
    // cross-currency destination-amount requirement is enforced here.
    let destinationAmountValue: number | undefined;
    if (crossCurrency) {
      destinationAmountValue = Number(destinationAmount);
      if (
        destinationAmount.trim().length === 0 ||
        !Number.isFinite(destinationAmountValue) ||
        destinationAmountValue <= 0
      ) {
        setDestinationAmountError('Destination amount must be greater than 0');
        return;
      }
    }
    setDestinationAmountError(null);

    try {
      await mutateAsync({
        sourceAccountId: parsed.sourceAccountId,
        destinationAccountId: parsed.destinationAccountId,
        amount: parsed.amount,
        date: parsed.date,
        description: parsed.description,
        ...(parsed.categoryId ? { categoryId: parsed.categoryId } : {}),
        ...(parsed.notes && parsed.notes.trim().length > 0 ? { notes: parsed.notes.trim() } : {}),
        ...(crossCurrency && destinationAmountValue !== undefined
          ? { destinationAmount: destinationAmountValue }
          : {}),
      });
      toast.success('Transfer recorded');
      resetForm();
      setOpen(false);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to record transfer';
      if (/source/i.test(message)) {
        setError('sourceAccountId', { message });
      } else if (/destination/i.test(message)) {
        setError('destinationAccountId', { message });
      } else if (/amount/i.test(message)) {
        setError('amount', { message });
      } else if (/description/i.test(message)) {
        setError('description', { message });
      } else {
        toast.error(message);
      }
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) resetForm();
      }}
    >
      {!isControlled && (
        <DialogTrigger asChild>
          <Button variant="outline" data-testid="new-transfer-button">
            <ArrowLeftRight className="h-4 w-4" />
            {triggerLabel ?? 'New transfer'}
          </Button>
        </DialogTrigger>
      )}
      <DialogContent>
        <DialogHeader>
          <DialogTitle>New transfer</DialogTitle>
          <DialogDescription>Move money between two of your accounts.</DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="transfer-source">Source account</Label>
            <Select
              value={sourceId}
              onValueChange={(v) => setValue('sourceAccountId', v, { shouldValidate: true })}
            >
              <SelectTrigger id="transfer-source" data-testid="transfer-source-select">
                <SelectValue placeholder="Select source account" />
              </SelectTrigger>
              <SelectContent>
                {eligibleAccounts.map((a) => (
                  <SelectItem key={a.id} value={a.id}>
                    {a.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {errors.sourceAccountId && (
              <p className="text-xs text-destructive" role="alert">
                {errors.sourceAccountId.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="transfer-destination">Destination account</Label>
            <Select
              value={destinationId}
              onValueChange={(v) => setValue('destinationAccountId', v, { shouldValidate: true })}
            >
              <SelectTrigger id="transfer-destination" data-testid="transfer-destination-select">
                <SelectValue placeholder="Select destination account" />
              </SelectTrigger>
              <SelectContent>
                {eligibleAccounts
                  .filter((a) => a.id !== sourceId)
                  .map((a) => (
                    <SelectItem key={a.id} value={a.id}>
                      {a.name}
                    </SelectItem>
                  ))}
              </SelectContent>
            </Select>
            {errors.destinationAccountId && (
              <p className="text-xs text-destructive" role="alert">
                {errors.destinationAccountId.message}
              </p>
            )}
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-2">
              <Label htmlFor="transfer-amount">Amount ({sourceCurrency})</Label>
              <Input
                id="transfer-amount"
                data-testid="transfer-amount-input"
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
              <Label htmlFor="transfer-date">Date</Label>
              <Input
                id="transfer-date"
                data-testid="transfer-date-input"
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
          </div>

          {crossCurrency && destinationCurrency && (
            <div className="space-y-2">
              <Label htmlFor="transfer-destination-amount">
                Destination amount ({destinationCurrency})
              </Label>
              <Input
                id="transfer-destination-amount"
                data-testid="transfer-destination-amount-input"
                type="number"
                step="0.01"
                min="0"
                value={destinationAmount}
                onChange={(e) => {
                  setDestinationAmount(e.target.value);
                  setDestinationAmountDirty(true);
                  setDestinationAmountError(null);
                }}
                aria-invalid={Boolean(destinationAmountError)}
              />
              {rateLabel && (
                <p className="text-xs text-muted-foreground" data-testid="transfer-rate">
                  {rateLabel}
                </p>
              )}
              {destinationAmountError && (
                <p className="text-xs text-destructive" role="alert">
                  {destinationAmountError}
                </p>
              )}
            </div>
          )}

          <div className="space-y-2">
            <Label htmlFor="transfer-category">Category (optional)</Label>
            <Select
              value={categoryId || ''}
              onValueChange={(v) => setValue('categoryId', v, { shouldValidate: true })}
            >
              <SelectTrigger id="transfer-category" data-testid="transfer-category-select">
                <SelectValue placeholder="Uncategorized" />
              </SelectTrigger>
              <SelectContent>
                {transferCategories.map((c) => (
                  <SelectItem key={c.id} value={c.id}>
                    {c.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-2">
            <Label htmlFor="transfer-description">Description</Label>
            <Textarea
              id="transfer-description"
              data-testid="transfer-description-input"
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
            <Label htmlFor="transfer-notes">Notes (optional)</Label>
            <Textarea
              id="transfer-notes"
              data-testid="transfer-notes-input"
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
            <Button type="submit" disabled={isPending} data-testid="transfer-submit-button">
              {isPending ? 'Recording...' : 'Record transfer'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
