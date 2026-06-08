'use client';

import { Tabs, TabsList, TabsTrigger } from '@/src/components/ui/tabs';
import type { ReportDirection } from '@/src/types/api';

interface DirectionToggleProps {
  value: ReportDirection;
  onChange: (next: ReportDirection) => void;
  testIdPrefix?: string;
  label?: string;
}

/**
 * Segmented toggle between Expense (default) and Income for the report
 * filters that take a `direction` query string parameter. Wraps the
 * shared shadcn `Tabs` primitive so focus/keyboard semantics come along
 * for free.
 */
export function DirectionToggle({
  value,
  onChange,
  testIdPrefix = 'direction',
  label = 'Direction',
}: DirectionToggleProps) {
  return (
    <div className="space-y-1.5">
      <span className="block text-xs text-muted-foreground" id={`${testIdPrefix}-label`}>
        {label}
      </span>
      <Tabs
        value={value}
        onValueChange={(v) => onChange(v as ReportDirection)}
        aria-labelledby={`${testIdPrefix}-label`}
      >
        <TabsList>
          <TabsTrigger value="Expense" data-testid={`${testIdPrefix}-expense`}>
            Expense
          </TabsTrigger>
          <TabsTrigger value="Income" data-testid={`${testIdPrefix}-income`}>
            Income
          </TabsTrigger>
        </TabsList>
      </Tabs>
    </div>
  );
}
