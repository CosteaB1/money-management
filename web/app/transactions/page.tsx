'use client';

import { FileUp } from 'lucide-react';
import Link from 'next/link';
import { useMemo, useState } from 'react';
import { PageHeader } from '@/src/components/page-header';
import { ExportCsvButton } from '@/src/components/reports/export-csv-button';
import { AddTransactionDialog } from '@/src/components/transactions/add-transaction-dialog';
import { CreateTransferDialog } from '@/src/components/transactions/create-transfer-dialog';
import {
  ALL_FILTER_VALUE,
  type TransactionFilterState,
  TransactionsFilters,
} from '@/src/components/transactions/transactions-filters';
import { TransactionsTable } from '@/src/components/transactions/transactions-table';
import { Button } from '@/src/components/ui/button';
import type { TransactionFilters } from '@/src/lib/api/transactions';
import { toIsoDateString } from '@/src/lib/utils/date';

function defaultFilters(): TransactionFilterState {
  const today = new Date();
  const from = new Date(today);
  from.setDate(from.getDate() - 30);
  return {
    accountId: ALL_FILTER_VALUE,
    from: toIsoDateString(from),
    to: toIsoDateString(today),
    categoryId: ALL_FILTER_VALUE,
    direction: 'all',
    transfer: 'all',
    adjustment: 'all',
  };
}

function toApiFilters(state: TransactionFilterState): TransactionFilters {
  const filters: TransactionFilters = {};
  if (state.accountId !== ALL_FILTER_VALUE) filters.accountId = state.accountId;
  if (state.from) filters.from = state.from;
  if (state.to) filters.to = state.to;
  if (state.categoryId !== ALL_FILTER_VALUE) filters.categoryIds = [state.categoryId];
  if (state.direction !== 'all') filters.direction = state.direction;
  if (state.transfer === 'transfers') filters.isTransfer = true;
  else if (state.transfer === 'exclude') filters.isTransfer = false;
  if (state.adjustment === 'adjustments') filters.isAdjustment = true;
  else if (state.adjustment === 'exclude') filters.isAdjustment = false;
  return filters;
}

export default function TransactionsPage() {
  const [filterState, setFilterState] = useState<TransactionFilterState>(defaultFilters);
  const [page, setPage] = useState(1);
  const apiFilters = useMemo(() => toApiFilters(filterState), [filterState]);

  const handleFilterChange = (next: TransactionFilterState) => {
    setFilterState(next);
    setPage(1);
  };

  const subtitle = useMemo(() => {
    const bits: string[] = [`${filterState.from} → ${filterState.to}`];
    if (filterState.direction !== 'all') bits.push(filterState.direction.toLowerCase());
    if (filterState.accountId !== ALL_FILTER_VALUE) bits.push('1 account');
    if (filterState.categoryId !== ALL_FILTER_VALUE) bits.push('1 category');
    return bits.join(' · ');
  }, [filterState]);

  return (
    <>
      <PageHeader
        title="Transactions"
        description={subtitle}
        actions={
          <div className="flex items-center gap-2">
            <Button asChild variant="outline" data-testid="import-pdf-button">
              <Link href="/transactions/import">
                <FileUp className="h-4 w-4" />
                Import from PDF
              </Link>
            </Button>
            <ExportCsvButton filters={apiFilters} />
            <CreateTransferDialog />
            <AddTransactionDialog />
          </div>
        }
      />
      <div className="space-y-4">
        <TransactionsFilters value={filterState} onChange={handleFilterChange} />
        <TransactionsTable
          filters={apiFilters}
          page={page}
          pageSize={25}
          onPageChange={setPage}
          allowDelete
          emptyAction={
            <div className="flex items-center gap-2">
              <AddTransactionDialog />
              <Button asChild variant="outline">
                <Link href="/transactions/import">
                  <FileUp className="h-4 w-4" />
                  Import from PDF
                </Link>
              </Button>
            </div>
          }
        />
      </div>
    </>
  );
}
