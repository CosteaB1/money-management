'use client';

import { useState } from 'react';
import { DateRangePicker } from '@/src/components/reports/date-range-picker';
import { DirectionToggle } from '@/src/components/reports/direction-toggle';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { Input } from '@/src/components/ui/input';
import { Label } from '@/src/components/ui/label';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/src/components/ui/table';
import { useTopPayees } from '@/src/lib/api/reports';
import { formatMoney } from '@/src/lib/utils/currency';
import { toIsoDateString } from '@/src/lib/utils/date';
import type { ReportDirection } from '@/src/types/api';

function defaultTrailingThreeMonths(): { from: string; to: string } {
  const today = new Date();
  const from = new Date(today);
  from.setMonth(from.getMonth() - 3);
  return { from: toIsoDateString(from), to: toIsoDateString(today) };
}

const DEFAULT_LIMIT = 10;

export function TopPayeesSection() {
  const [direction, setDirection] = useState<ReportDirection>('Expense');
  const [range, setRange] = useState<{ from: string; to: string }>(defaultTrailingThreeMonths);
  const [limit, setLimit] = useState<number>(DEFAULT_LIMIT);

  const { data, isLoading, isError } = useTopPayees({
    from: range.from,
    to: range.to,
    direction,
    limit,
  });

  return (
    <Card data-testid="top-payees-section" className="h-full">
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-medium text-muted-foreground">Top payees</CardTitle>
        <div className="flex flex-wrap items-end gap-4 pt-3">
          <DirectionToggle
            value={direction}
            onChange={setDirection}
            testIdPrefix="payees-direction"
          />
          <DateRangePicker
            from={range.from}
            to={range.to}
            onChange={setRange}
            idPrefix="payees-range"
            testIdPrefix="payees-range"
          />
          <div className="space-y-1.5">
            <Label htmlFor="payees-limit" className="text-xs text-muted-foreground">
              Limit
            </Label>
            <Input
              id="payees-limit"
              type="number"
              min={1}
              max={50}
              step={1}
              value={limit}
              onChange={(e) => {
                const next = Number(e.target.value);
                if (Number.isFinite(next) && next > 0) setLimit(Math.floor(next));
              }}
              className="w-24"
              data-testid="payees-limit"
            />
          </div>
        </div>
      </CardHeader>
      <CardContent>
        {isError ? (
          <p className="text-sm text-muted-foreground" data-testid="top-payees-section-error">
            Failed to load top payees.
          </p>
        ) : isLoading || !data ? (
          <div
            className="h-48 w-full animate-pulse rounded bg-muted"
            role="status"
            aria-label="Loading"
            data-testid="top-payees-section-loading"
          />
        ) : data.length === 0 ? (
          <p className="text-sm text-muted-foreground" data-testid="top-payees-section-empty">
            No data in this range yet.
          </p>
        ) : (
          <div className="overflow-hidden rounded-lg border">
            <Table data-testid="top-payees-table">
              <TableHeader>
                <TableRow>
                  <TableHead className="w-12">#</TableHead>
                  <TableHead>Payee</TableHead>
                  <TableHead className="text-right">Amount</TableHead>
                  <TableHead className="text-right">Tx</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.map((row, idx) => (
                  <TableRow
                    key={`${row.payee}-${row.originalDescription}`}
                    data-testid="top-payees-row"
                  >
                    <TableCell className="tabular-nums text-muted-foreground">{idx + 1}</TableCell>
                    <TableCell>
                      <div className="font-medium">{row.payee}</div>
                      <div className="text-xs text-muted-foreground">{row.originalDescription}</div>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {formatMoney(row.amountMdl, 'MDL')}
                    </TableCell>
                    <TableCell className="text-right tabular-nums text-muted-foreground">
                      {row.transactionCount}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
