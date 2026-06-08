'use client';

import { Input } from '@/src/components/ui/input';
import { Label } from '@/src/components/ui/label';

/**
 * Resolution of the two `<input>` controls in `DateRangePicker`.
 *
 * - `'day'`  → `<input type="date">`, ISO `yyyy-MM-dd` strings.
 * - `'month'`→ `<input type="month">`, ISO `yyyy-MM` strings.
 */
export type DateRangeResolution = 'day' | 'month';

interface DateRangePickerProps {
  fromLabel?: string;
  toLabel?: string;
  from: string;
  to: string;
  onChange: (next: { from: string; to: string }) => void;
  resolution?: DateRangeResolution;
  /** Stable id prefix; used to associate the `<label>`s with the inputs. */
  idPrefix: string;
  testIdPrefix?: string;
}

/**
 * Lightweight two-field date range picker. Controlled — the parent owns the
 * `from`/`to` strings and reacts to `onChange`. Native HTML inputs only;
 * no popovers, no calendar widget, just `<input type="date|month">`.
 *
 * Accessibility: every input is associated with a visible `<label>` via
 * `htmlFor`/`id`, hitting WCAG 2.2 3.3.2. The native picker inherits the
 * UA's focus styling and keyboard navigation.
 */
export function DateRangePicker({
  fromLabel = 'From',
  toLabel = 'To',
  from,
  to,
  onChange,
  resolution = 'day',
  idPrefix,
  testIdPrefix,
}: DateRangePickerProps) {
  const inputType = resolution === 'month' ? 'month' : 'date';
  const fromId = `${idPrefix}-from`;
  const toId = `${idPrefix}-to`;
  const testFrom = testIdPrefix ? `${testIdPrefix}-from` : undefined;
  const testTo = testIdPrefix ? `${testIdPrefix}-to` : undefined;
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1.5">
        <Label htmlFor={fromId} className="text-xs text-muted-foreground">
          {fromLabel}
        </Label>
        <Input
          id={fromId}
          type={inputType}
          value={from}
          data-testid={testFrom}
          onChange={(e) => onChange({ from: e.target.value, to })}
        />
      </div>
      <div className="space-y-1.5">
        <Label htmlFor={toId} className="text-xs text-muted-foreground">
          {toLabel}
        </Label>
        <Input
          id={toId}
          type={inputType}
          value={to}
          data-testid={testTo}
          onChange={(e) => onChange({ from, to: e.target.value })}
        />
      </div>
    </div>
  );
}
