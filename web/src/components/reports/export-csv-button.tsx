'use client';

import { Download } from 'lucide-react';
import { Button } from '@/src/components/ui/button';
import type { TransactionFilters } from '@/src/lib/api/transactions';

interface ExportCsvButtonProps {
  filters: TransactionFilters;
  /**
   * Override the API base URL — only used by tests, real callers should
   * leave this undefined so the env var drives it.
   */
  apiBaseUrl?: string;
}

const DEFAULT_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? '';

/**
 * Build a `/reports/transactions.csv?…` URL that mirrors the current
 * `TransactionFilters` shape. Exported for testing — the component itself
 * uses it internally.
 */
export function buildExportCsvUrl(
  filters: TransactionFilters,
  baseUrl: string = DEFAULT_BASE_URL,
): string {
  const params = new URLSearchParams();
  if (filters.accountId) params.set('accountId', filters.accountId);
  if (filters.from) params.set('from', filters.from);
  if (filters.to) params.set('to', filters.to);
  if (filters.direction) params.set('direction', filters.direction);
  if (filters.categoryIds && filters.categoryIds.length > 0) {
    for (const id of filters.categoryIds) params.append('categoryId', id);
  }
  if (filters.isTransfer !== undefined) params.set('isTransfer', String(filters.isTransfer));
  if (filters.isAdjustment !== undefined) {
    params.set('isAdjustment', String(filters.isAdjustment));
  }
  const qs = params.toString();
  return `${baseUrl}/reports/transactions.csv${qs ? `?${qs}` : ''}`;
}

/**
 * Triggers a browser download of the filtered transaction list as CSV by
 * synthesizing a hidden `<a download>` and clicking it. Picked over
 * `window.location.href = url` so the user stays on the transactions page
 * even when the browser surfaces the download via a top-bar prompt.
 */
export function ExportCsvButton({ filters, apiBaseUrl }: ExportCsvButtonProps) {
  const handleClick = () => {
    if (typeof document === 'undefined') return;
    const href = buildExportCsvUrl(filters, apiBaseUrl);
    const anchor = document.createElement('a');
    anchor.href = href;
    anchor.rel = 'noopener';
    // Suggesting a filename — the backend's Content-Disposition still wins
    // when present, but this keeps the default sane when it's absent.
    anchor.download = 'transactions.csv';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  };

  return (
    <Button type="button" variant="outline" onClick={handleClick} data-testid="export-csv-button">
      <Download className="h-4 w-4" aria-hidden />
      Export CSV
    </Button>
  );
}
