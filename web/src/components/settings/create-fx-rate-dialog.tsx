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
import { useCreateFxRate } from '@/src/lib/api/fx-rates';
import { toIsoDateString } from '@/src/lib/utils/date';

const CURRENCY_OPTIONS = ['MDL', 'USD', 'EUR', 'RON', 'GBP'] as const;

const schema = z
  .object({
    fromCurrency: z.string().regex(/^[A-Z]{3}$/, 'From currency must be a 3-letter ISO code'),
    toCurrency: z.string().regex(/^[A-Z]{3}$/, 'To currency must be a 3-letter ISO code'),
    rate: z.coerce
      .number({ invalid_type_error: 'Rate must be a number' })
      .positive('Rate must be greater than 0'),
    asOf: z.string().min(1, 'As-of date is required'),
  })
  .refine((d) => d.fromCurrency !== d.toCurrency, {
    path: ['toCurrency'],
    message: 'From and To currencies must differ',
  });

type FormValues = z.input<typeof schema>;
type ParsedValues = z.output<typeof schema>;

export function CreateFxRateDialog() {
  const [open, setOpen] = useState(false);
  const { mutateAsync, isPending } = useCreateFxRate();

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
      fromCurrency: 'USD',
      toCurrency: 'MDL',
      rate: 0,
      asOf: toIsoDateString(new Date()),
    },
  });

  const fromCurrency = watch('fromCurrency');
  const toCurrency = watch('toCurrency');

  const onSubmit = handleSubmit(async (values) => {
    const parsed = values as unknown as ParsedValues;
    try {
      await mutateAsync({
        fromCurrency: parsed.fromCurrency,
        toCurrency: parsed.toCurrency,
        rate: parsed.rate,
        asOf: parsed.asOf,
      });
      toast.success('FX rate added');
      reset();
      setOpen(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to add FX rate');
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
        <Button data-testid="add-fx-rate-button">
          <Plus className="h-4 w-4" />
          Add rate
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add FX rate</DialogTitle>
          <DialogDescription>
            Record an exchange rate used to convert balances and transactions into MDL.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={onSubmit} className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-2">
              <Label htmlFor="fx-from-currency">From</Label>
              <Select
                value={fromCurrency}
                onValueChange={(v) => setValue('fromCurrency', v, { shouldValidate: true })}
              >
                <SelectTrigger id="fx-from-currency" data-testid="fx-from-currency-select">
                  <SelectValue placeholder="From" />
                </SelectTrigger>
                <SelectContent>
                  {CURRENCY_OPTIONS.map((code) => (
                    <SelectItem key={code} value={code}>
                      {code}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {/* Defensive: the From Select only emits valid 3-letter codes. */}
              {/* v8 ignore start */}
              {errors.fromCurrency && (
                <p className="text-xs text-destructive" role="alert">
                  {errors.fromCurrency.message}
                </p>
              )}
              {/* v8 ignore stop */}
            </div>

            <div className="space-y-2">
              <Label htmlFor="fx-to-currency">To</Label>
              <Select
                value={toCurrency}
                onValueChange={(v) => setValue('toCurrency', v, { shouldValidate: true })}
              >
                <SelectTrigger id="fx-to-currency" data-testid="fx-to-currency-select">
                  <SelectValue placeholder="To" />
                </SelectTrigger>
                <SelectContent>
                  {CURRENCY_OPTIONS.map((code) => (
                    <SelectItem key={code} value={code}>
                      {code}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {errors.toCurrency && (
                <p className="text-xs text-destructive" role="alert">
                  {errors.toCurrency.message}
                </p>
              )}
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-2">
              <Label htmlFor="fx-rate">Rate</Label>
              <Input
                id="fx-rate"
                data-testid="fx-rate-input"
                type="number"
                step="0.0001"
                min="0"
                {...register('rate')}
                aria-invalid={Boolean(errors.rate)}
                aria-describedby={errors.rate ? 'fx-rate-error' : undefined}
              />
              {errors.rate && (
                <p id="fx-rate-error" className="text-xs text-destructive" role="alert">
                  {errors.rate.message}
                </p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="fx-as-of">As of</Label>
              <Input
                id="fx-as-of"
                data-testid="fx-as-of-input"
                type="date"
                {...register('asOf')}
                aria-invalid={Boolean(errors.asOf)}
              />
              {errors.asOf && (
                <p className="text-xs text-destructive" role="alert">
                  {errors.asOf.message}
                </p>
              )}
            </div>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="fx-rate-submit-button">
              {isPending ? 'Adding...' : 'Add rate'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
