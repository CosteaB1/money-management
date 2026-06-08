'use client';

import { useMemo, useState } from 'react';
import { Cell, Legend, Pie, PieChart, ResponsiveContainer, Tooltip } from 'recharts';
import { DateRangePicker } from '@/src/components/reports/date-range-picker';
import { DirectionToggle } from '@/src/components/reports/direction-toggle';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/src/components/ui/table';
import { useCategoryBreakdown } from '@/src/lib/api/reports';
import { formatMoney } from '@/src/lib/utils/currency';
import { toIsoDateString } from '@/src/lib/utils/date';
import type { CategoryBreakdownItem, ReportDirection } from '@/src/types/api';

// Hand-picked, color-blind-friendly-ish palette. We rotate through these
// in `items` order — the backend already sorts desc by amount so the
// top slice always gets the first color.
const SLICE_COLORS = [
  'var(--color-chart-1)',
  'var(--color-chart-2)',
  'var(--color-chart-3)',
  'var(--color-chart-4)',
  'var(--color-chart-5)',
  '#f59e0b',
  '#8b5cf6',
  '#ec4899',
  '#14b8a6',
  '#64748b',
];

const percentFormatter = new Intl.NumberFormat('en-MD', {
  style: 'percent',
  maximumFractionDigits: 1,
});

function defaultCurrentMonthRange(): { from: string; to: string } {
  const today = new Date();
  const start = new Date(today.getFullYear(), today.getMonth(), 1);
  return { from: toIsoDateString(start), to: toIsoDateString(today) };
}

export function CategoryBreakdownSection() {
  const [direction, setDirection] = useState<ReportDirection>('Expense');
  const [range, setRange] = useState<{ from: string; to: string }>(defaultCurrentMonthRange);

  const { data, isLoading, isError } = useCategoryBreakdown({
    from: range.from,
    to: range.to,
    direction,
  });

  const items: CategoryBreakdownItem[] = useMemo(() => data?.items ?? [], [data?.items]);

  return (
    <Card data-testid="category-breakdown-section" className="h-full">
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-medium text-muted-foreground">
          Category breakdown
        </CardTitle>
        <div className="flex flex-wrap items-end gap-4 pt-3">
          <DirectionToggle
            value={direction}
            onChange={setDirection}
            testIdPrefix="category-direction"
          />
          <DateRangePicker
            from={range.from}
            to={range.to}
            onChange={setRange}
            idPrefix="category-range"
            testIdPrefix="category-range"
          />
        </div>
      </CardHeader>
      <CardContent>
        {isError ? (
          <p
            className="text-sm text-muted-foreground"
            data-testid="category-breakdown-section-error"
          >
            Failed to load category breakdown.
          </p>
        ) : isLoading || !data ? (
          <div
            className="h-72 w-full animate-pulse rounded bg-muted"
            role="status"
            aria-label="Loading"
            data-testid="category-breakdown-section-loading"
          />
        ) : items.length === 0 ? (
          <p
            className="text-sm text-muted-foreground"
            data-testid="category-breakdown-section-empty"
          >
            No data in this range yet.
          </p>
        ) : (
          <>
            <div className="grid gap-6 md:grid-cols-2">
              <div
                className="h-72 w-full"
                role="img"
                aria-label={`${direction} share by category`}
                data-testid="category-breakdown-chart"
              >
                <ul className="sr-only" data-testid="category-breakdown-points">
                  {items.map((it) => (
                    <li
                      key={it.categoryId ?? 'uncategorized'}
                      data-testid="category-breakdown-point"
                    >
                      {it.categoryName}: {formatMoney(it.amountMdl, 'MDL')} (
                      {percentFormatter.format(it.percentage)}, {it.transactionCount} tx)
                    </li>
                  ))}
                </ul>
                <ResponsiveContainer width="100%" height="100%">
                  <PieChart>
                    <Pie
                      data={items}
                      dataKey="amountMdl"
                      nameKey="categoryName"
                      innerRadius={50}
                      outerRadius={90}
                      paddingAngle={2}
                      isAnimationActive={false}
                    >
                      {items.map((it, idx) => (
                        <Cell
                          key={it.categoryId ?? 'uncategorized'}
                          fill={SLICE_COLORS[idx % SLICE_COLORS.length]}
                        />
                      ))}
                    </Pie>
                    <Tooltip
                      formatter={(value: number) => formatMoney(value, 'MDL')}
                      contentStyle={{
                        background: 'var(--color-popover)',
                        border: '1px solid var(--color-border)',
                        fontSize: 12,
                      }}
                    />
                    <Legend wrapperStyle={{ fontSize: 12 }} />
                  </PieChart>
                </ResponsiveContainer>
              </div>

              <div className="overflow-hidden rounded-lg border">
                <Table data-testid="category-breakdown-table">
                  <TableHeader>
                    <TableRow>
                      <TableHead>Category</TableHead>
                      <TableHead className="text-right">Amount</TableHead>
                      <TableHead className="text-right">%</TableHead>
                      <TableHead className="text-right">Tx</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {items.map((it) => (
                      <TableRow
                        key={it.categoryId ?? 'uncategorized'}
                        data-testid="category-breakdown-row"
                        data-uncategorized={it.categoryId === null ? 'true' : undefined}
                      >
                        <TableCell className="font-medium">{it.categoryName}</TableCell>
                        <TableCell className="text-right tabular-nums">
                          {formatMoney(it.amountMdl, 'MDL')}
                        </TableCell>
                        <TableCell className="text-right tabular-nums text-muted-foreground">
                          {percentFormatter.format(it.percentage)}
                        </TableCell>
                        <TableCell className="text-right tabular-nums text-muted-foreground">
                          {it.transactionCount}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
            </div>

            <p
              className="mt-3 text-xs text-muted-foreground"
              data-testid="category-breakdown-total"
            >
              Total: {formatMoney(data.totalMdl, 'MDL')}
            </p>

            {data.missingFxRate && (
              <p
                className="mt-2 text-xs text-amber-600 dark:text-amber-400"
                data-testid="category-breakdown-missing-fx"
              >
                Some transactions couldn&apos;t be converted to MDL — totals may be incomplete.
              </p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
