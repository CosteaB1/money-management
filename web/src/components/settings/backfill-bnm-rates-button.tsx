'use client';

import { subMonths } from 'date-fns';
import { History, Loader2 } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
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
import { useAccounts } from '@/src/lib/api/accounts';
import { useBackfillBnmRates } from '@/src/lib/api/fx-rates';
import { toIsoDateString } from '@/src/lib/utils/date';
import type { BackfillBnmRatesRequest } from '@/src/types/api';

/**
 * "Backfill history" action that opens a dialog to pull official BNM rates
 * for every business day in a chosen date range. Sits beside the
 * "Refresh from BNM" button.
 *
 * The From field defaults to the earliest account opening date (so a fresh
 * backfill covers the whole history of money in the app); when there are no
 * accounts yet it falls back to ~12 months before today. The default is
 * computed lazily each time the dialog opens.
 *
 * The backend loops the range server-side and can take up to a minute — the
 * submit button shows a spinner while pending and there is no client timeout.
 * A bad range comes back as a 400 ProblemDetails whose `detail` is surfaced
 * verbatim in the error toast.
 */
export function BackfillBnmRatesButton() {
  const [open, setOpen] = useState(false);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  // Include archived accounts so the earliest opening date is honest even if
  // the oldest account has since been archived.
  const { data: accounts } = useAccounts(true);
  const { mutateAsync, isPending } = useBackfillBnmRates();

  const computeDefaultFrom = (): string => {
    const openingDates = (accounts ?? [])
      .map((a) => a.openingDate)
      .filter((d): d is string => Boolean(d));
    if (openingDates.length > 0) {
      // yyyy-MM-dd strings sort lexicographically the same as chronologically.
      return openingDates.reduce((min, d) => (d < min ? d : min));
    }
    return toIsoDateString(subMonths(new Date(), 12));
  };

  const handleOpenChange = (next: boolean) => {
    setOpen(next);
    if (next) {
      // Compute the default once, when the dialog opens.
      setFrom(computeDefaultFrom());
      setTo('');
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      // Only carry `to` when the user actually picked an end date — omitting
      // it lets the backend default to today. `exactOptionalPropertyTypes`
      // forbids assigning `undefined` to the `string | null` field, so we
      // build the request conditionally rather than passing `to: undefined`.
      const input: BackfillBnmRatesRequest = to ? { from, to } : { from };
      const result = await mutateAsync(input);
      const { daysProcessed, inserted, updated } = result;
      toast.success(`Backfilled ${daysProcessed} days · ${inserted} added, ${updated} updated`);
      setOpen(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Backfill failed');
    }
  };

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogTrigger asChild>
        <Button
          variant="outline"
          data-testid="backfill-bnm-rates-button"
          aria-label="Backfill historical FX rates from BNM"
        >
          <History className="h-4 w-4" />
          Backfill history
        </Button>
      </DialogTrigger>
      <DialogContent data-testid="backfill-bnm-dialog">
        <DialogHeader>
          <DialogTitle>Backfill history</DialogTitle>
          <DialogDescription>
            Pulls official BNM rates for every business day in the range. This can take a minute.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-2">
              <Label htmlFor="backfill-from">From</Label>
              <Input
                id="backfill-from"
                data-testid="backfill-from-input"
                type="date"
                required
                value={from}
                onChange={(e) => setFrom(e.target.value)}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="backfill-to">To (optional, defaults to today)</Label>
              <Input
                id="backfill-to"
                data-testid="backfill-to-input"
                type="date"
                value={to}
                onChange={(e) => setTo(e.target.value)}
              />
            </div>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isPending} data-testid="backfill-submit-button">
              {isPending ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" data-testid="backfill-spinner" />
                  Backfilling…
                </>
              ) : (
                'Backfill'
              )}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
