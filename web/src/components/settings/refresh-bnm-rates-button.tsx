'use client';

import { Loader2, RefreshCw } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/src/components/ui/button';
import { useRefreshBnmRates } from '@/src/lib/api/fx-rates';

/**
 * Triggers a synchronous BNM refresh and surfaces the result via sonner
 * toasts. No body is sent — the backend defaults to today (UTC) plus
 * every currency the user currently holds.
 *
 * The button is disabled and shows a spinner while the request is in
 * flight (BNM can take up to ~10s to respond).
 */
export function RefreshBnmRatesButton() {
  const { mutateAsync, isPending } = useRefreshBnmRates();

  const handleClick = async () => {
    try {
      const result = await mutateAsync({});
      const { inserted, updated, skipped } = result;
      if (inserted + updated === 0) {
        toast.success('FX rates are up to date.');
      } else {
        toast.success(`Refreshed: ${inserted} added, ${updated} updated, ${skipped} unchanged`);
      }
    } catch {
      toast.error('Failed to refresh from BNM. Try again later.');
    }
  };

  return (
    <Button
      variant="outline"
      onClick={handleClick}
      disabled={isPending}
      data-testid="refresh-bnm-rates-button"
      aria-label="Refresh FX rates from BNM"
    >
      {isPending ? (
        <Loader2 className="h-4 w-4 animate-spin" data-testid="refresh-bnm-spinner" />
      ) : (
        <RefreshCw className="h-4 w-4" />
      )}
      {isPending ? 'Refreshing...' : 'Refresh from BNM'}
    </Button>
  );
}
