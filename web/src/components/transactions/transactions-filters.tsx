'use client';

import { Input } from '@/src/components/ui/input';
import { Label } from '@/src/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/src/components/ui/select';
import { Tabs, TabsList, TabsTrigger } from '@/src/components/ui/tabs';
import { useAccounts } from '@/src/lib/api/accounts';
import { useCategories } from '@/src/lib/api/categories';
import type { TransactionDirection } from '@/src/types/api';

const ALL = 'all';

/**
 * Tri-state UI filter for the transfer flag.
 * - 'all' → backend gets `isTransfer=undefined` (no filter)
 * - 'transfers' → `isTransfer=true`
 * - 'exclude' → `isTransfer=false`
 */
export type TransferFilter = 'all' | 'transfers' | 'exclude';

/**
 * Tri-state UI filter for the adjustment flag — parallels `TransferFilter`.
 * - 'all' → no filter
 * - 'adjustments' → only balance-adjustment rows
 * - 'exclude' → exclude balance-adjustment rows
 */
export type AdjustmentFilter = 'all' | 'adjustments' | 'exclude';

export interface TransactionFilterState {
  accountId: string;
  from: string;
  to: string;
  categoryId: string;
  direction: 'all' | TransactionDirection;
  transfer: TransferFilter;
  adjustment: AdjustmentFilter;
}

interface Props {
  value: TransactionFilterState;
  onChange: (next: TransactionFilterState) => void;
}

export function TransactionsFilters({ value, onChange }: Props) {
  const accountsQuery = useAccounts(false);
  const categoriesQuery = useCategories({ includeArchived: false });

  return (
    <div className="rounded-lg border bg-card p-4">
      <div className="grid grid-cols-1 gap-3 md:grid-cols-4">
        <div className="space-y-1.5">
          <Label htmlFor="filter-account" className="text-xs text-muted-foreground">
            Account
          </Label>
          <Select
            value={value.accountId}
            onValueChange={(v) => onChange({ ...value, accountId: v })}
          >
            <SelectTrigger id="filter-account" data-testid="filter-account">
              <SelectValue placeholder="All accounts" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>All accounts</SelectItem>
              {accountsQuery.data?.map((a) => (
                <SelectItem key={a.id} value={a.id}>
                  {a.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="filter-from" className="text-xs text-muted-foreground">
            From
          </Label>
          <Input
            id="filter-from"
            data-testid="filter-from"
            type="date"
            value={value.from}
            onChange={(e) => onChange({ ...value, from: e.target.value })}
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="filter-to" className="text-xs text-muted-foreground">
            To
          </Label>
          <Input
            id="filter-to"
            data-testid="filter-to"
            type="date"
            value={value.to}
            onChange={(e) => onChange({ ...value, to: e.target.value })}
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="filter-category" className="text-xs text-muted-foreground">
            Category
          </Label>
          <Select
            value={value.categoryId}
            onValueChange={(v) => onChange({ ...value, categoryId: v })}
          >
            <SelectTrigger id="filter-category" data-testid="filter-category">
              <SelectValue placeholder="All categories" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>All categories</SelectItem>
              {categoriesQuery.data?.map((c) => (
                <SelectItem key={c.id} value={c.id}>
                  {c.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      <div className="mt-4 flex flex-wrap items-center gap-4">
        <Tabs
          value={value.direction}
          onValueChange={(v) =>
            onChange({ ...value, direction: v as 'all' | TransactionDirection })
          }
        >
          <TabsList>
            <TabsTrigger value="all" data-testid="filter-direction-all">
              All
            </TabsTrigger>
            <TabsTrigger value="Income" data-testid="filter-direction-income">
              Income
            </TabsTrigger>
            <TabsTrigger value="Expense" data-testid="filter-direction-expense">
              Expense
            </TabsTrigger>
          </TabsList>
        </Tabs>

        <Tabs
          value={value.transfer}
          onValueChange={(v) => onChange({ ...value, transfer: v as TransferFilter })}
        >
          <TabsList>
            <TabsTrigger value="all" data-testid="filter-transfer-all">
              All
            </TabsTrigger>
            <TabsTrigger value="transfers" data-testid="filter-transfer-only">
              Transfers only
            </TabsTrigger>
            <TabsTrigger value="exclude" data-testid="filter-transfer-exclude">
              Exclude transfers
            </TabsTrigger>
          </TabsList>
        </Tabs>

        <Tabs
          value={value.adjustment}
          onValueChange={(v) => onChange({ ...value, adjustment: v as AdjustmentFilter })}
        >
          <TabsList>
            <TabsTrigger value="all" data-testid="filter-adjustment-all">
              All
            </TabsTrigger>
            <TabsTrigger value="adjustments" data-testid="filter-adjustment-only">
              Adjustments only
            </TabsTrigger>
            <TabsTrigger value="exclude" data-testid="filter-adjustment-exclude">
              Exclude adjustments
            </TabsTrigger>
          </TabsList>
        </Tabs>
      </div>
    </div>
  );
}

export const ALL_FILTER_VALUE = ALL;
