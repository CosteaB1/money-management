'use client';

import { Loader2, RefreshCw } from 'lucide-react';
import { Button } from '@/src/components/ui/button';
import { useRebuildBudgetPeriods } from '@/src/lib/api/budgets';

/**
 * Recomputes every budget's period rows from the underlying transactions.
 * The hook owns the success/error toasts and cache invalidation; this
 * component just drives it and reflects the pending state.
 *
 * Disabled with a spinner while in flight. Sits beside "Add budget" in the
 * Budgets page header.
 */
export function RebuildPeriodsButton() {
  const { mutate, isPending } = useRebuildBudgetPeriods();

  return (
    <Button
      type="button"
      variant="outline"
      onClick={() => mutate()}
      disabled={isPending}
      data-testid="rebuild-budgets-button"
      aria-label="Rebuild budget periods"
    >
      {isPending ? (
        <Loader2 className="h-4 w-4 animate-spin" data-testid="rebuild-budgets-spinner" />
      ) : (
        <RefreshCw className="h-4 w-4" />
      )}
      {isPending ? 'Rebuilding...' : 'Rebuild periods'}
    </Button>
  );
}
