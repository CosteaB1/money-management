'use client';

import type { UseFormRegisterReturn } from 'react-hook-form';
import { Input } from '@/src/components/ui/input';
import { Label } from '@/src/components/ui/label';

/**
 * The forced valuation. **This field is what makes the whole model work.**
 *
 * Every unit is minted or burned at `NAV = poolValue / unitsOutstanding`, and
 * the app's idea of `poolValue` is only ever as fresh as the last number the
 * user typed. Price a subscription against a two-week-old balance in an
 * account returning double digits a month and the participant buys in at the
 * wrong price — permanently, and invisibly, transferring value between them
 * and the owner. The design's own words: *"the user must type the exchange's
 * real total immediately before any money crosses the boundary, and the app
 * should make that a hard block."*
 *
 * So it is required on every route where cash moves, and the handler marks the
 * account to it **before** striking the NAV, inside one save.
 *
 * The "including BNB" instruction is not a detail. Whether BNB counts as pool
 * property is a convention, and either convention nets out over time — but
 * *switching* between them manufactures phantom profit, which then gets paid
 * out to somebody. Include it, always.
 */
export function PoolValueNowField({
  id,
  currency,
  registration,
  error,
  optional = false,
}: {
  id: string;
  /** The pool's currency — the total must be typed in it, never converted. */
  currency: string;
  registration: UseFormRegisterReturn;
  error?: string | undefined;
  /**
   * Only ever true for a cost reimbursement, where no cash crosses the
   * boundary and the backend contract makes the value nullable. Everywhere
   * else this is a hard requirement.
   */
  optional?: boolean;
}) {
  return (
    <div className="space-y-2 rounded-md border border-amber-500/40 bg-amber-500/5 p-3">
      <Label htmlFor={id} className="text-sm font-semibold">
        Exchange total right now ({currency}){optional ? ' — optional' : ' *'}
      </Label>
      <Input
        id={id}
        data-testid={`${id}-input`}
        type="number"
        step="0.01"
        min="0"
        inputMode="decimal"
        {...registration}
        aria-invalid={Boolean(error)}
        aria-describedby={`${id}-help${error ? ` ${id}-error` : ''}`}
      />
      <p id={`${id}-help`} className="text-xs text-muted-foreground">
        Read the exchange right now and type its <strong>whole</strong> total across spot, futures,
        earn and fiat — <strong>including BNB</strong>. The pool is marked to this figure before any
        share price is struck, so a stale number silently misprices everyone.
        {optional
          ? ' Optional here because no cash moves, but supplying it still gives a fresher price.'
          : ' Required: no valuation, no movement.'}
      </p>
      {error && (
        <p id={`${id}-error`} className="text-xs text-destructive" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
