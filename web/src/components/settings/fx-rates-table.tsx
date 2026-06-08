'use client';

import { Trash2 } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/src/components/ui/badge';
import { Button } from '@/src/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/src/components/ui/table';
import { useDeleteFxRate, useFxRates } from '@/src/lib/api/fx-rates';
import { formatShortDate } from '@/src/lib/utils/date';
import type { FxRateSource } from '@/src/types/api';

interface SourceBadgeProps {
  source: FxRateSource;
}

function SourceBadge({ source }: SourceBadgeProps) {
  if (source === 'BnmAuto') {
    return (
      <Badge
        variant="outline"
        className="border-blue-500/40 text-blue-500 dark:text-blue-400"
        data-testid="fx-rate-source-badge"
        data-source="BnmAuto"
      >
        BNM
      </Badge>
    );
  }
  return (
    <Badge
      variant="outline"
      className="text-muted-foreground"
      data-testid="fx-rate-source-badge"
      data-source="Manual"
    >
      Manual
    </Badge>
  );
}

export function FxRatesTable() {
  const [page, setPage] = useState(1);
  const { data, isLoading, isError } = useFxRates(page, 25);
  const remove = useDeleteFxRate();

  const handleDelete = async (id: string, label: string) => {
    try {
      await remove.mutateAsync(id);
      toast.success(`Removed ${label}`);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to delete rate');
    }
  };

  return (
    <div className="rounded-lg border">
      <Table data-testid="fx-rates-table">
        <TableHeader>
          <TableRow>
            <TableHead>From</TableHead>
            <TableHead>To</TableHead>
            <TableHead className="text-right">Rate</TableHead>
            <TableHead>Source</TableHead>
            <TableHead>As of</TableHead>
            <TableHead className="w-12 text-right">
              <span className="sr-only">Actions</span>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isError ? (
            <TableRow>
              <TableCell colSpan={6} className="text-center text-destructive">
                Failed to load FX rates.
              </TableCell>
            </TableRow>
          ) : isLoading || !data ? (
            <TableRow>
              <TableCell colSpan={6} className="text-center text-muted-foreground">
                Loading...
              </TableCell>
            </TableRow>
          ) : data.items.length === 0 ? (
            <TableRow>
              <TableCell colSpan={6} className="text-center text-muted-foreground">
                No FX rates yet. Click &ldquo;Add rate&rdquo; to start.
              </TableCell>
            </TableRow>
          ) : (
            data.items.map((rate) => {
              const label = `${rate.fromCurrency}→${rate.toCurrency} @ ${rate.asOf}`;
              const isBnm = rate.source === 'BnmAuto';
              const deleteTitle = isBnm
                ? 'BNM rates will be re-fetched on the next refresh.'
                : undefined;
              return (
                <TableRow key={rate.id} data-testid="fx-rate-row">
                  <TableCell className="font-medium">{rate.fromCurrency}</TableCell>
                  <TableCell>{rate.toCurrency}</TableCell>
                  <TableCell className="text-right tabular-nums">{rate.rate.toFixed(2)}</TableCell>
                  <TableCell>
                    <SourceBadge source={rate.source} />
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {formatShortDate(rate.asOf)}
                  </TableCell>
                  <TableCell className="text-right">
                    <Button
                      variant="ghost"
                      size="icon"
                      aria-label={`Delete rate ${label}`}
                      data-testid="delete-fx-rate"
                      title={deleteTitle}
                      onClick={() => handleDelete(rate.id, label)}
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </TableCell>
                </TableRow>
              );
            })
          )}
        </TableBody>
      </Table>
      {data && data.totalPages > 1 && (
        <div className="flex items-center justify-between border-t px-4 py-3 text-sm text-muted-foreground">
          <span>
            {data.totalCount} rate{data.totalCount !== 1 ? 's' : ''}
          </span>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => setPage(data.pageNumber - 1)}
              disabled={data.pageNumber <= 1}
              className="rounded px-2 py-1 hover:bg-muted disabled:cursor-not-allowed disabled:opacity-40"
              aria-label="Previous page"
            >
              ← Prev
            </button>
            <span>
              Page {data.pageNumber} of {data.totalPages}
            </span>
            <button
              type="button"
              onClick={() => setPage(data.pageNumber + 1)}
              disabled={data.pageNumber >= data.totalPages}
              className="rounded px-2 py-1 hover:bg-muted disabled:cursor-not-allowed disabled:opacity-40"
              aria-label="Next page"
            >
              Next →
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
