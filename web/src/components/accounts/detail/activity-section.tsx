'use client';

import { useState } from 'react';
import { TransactionsTable } from '@/src/components/transactions/transactions-table';
import { Tabs, TabsList, TabsTrigger } from '@/src/components/ui/tabs';
import type { TransactionFilters } from '@/src/lib/api/transactions';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';

const ACTIVITY_PAGE_SIZE = 25;

type ActivityTab = 'All' | 'Contributions' | 'Withdrawals' | 'Adjustments' | 'Other';

/**
 * Maps a UI tab to the corresponding `useTransactions` filter shape.
 * Tabs are presets — not free-form filters — so the user can switch
 * lenses without scrolling through the global filter bar.
 *
 *   - Contributions = transfer legs landing on this account (Income side)
 *   - Withdrawals   = transfer legs leaving this account (Expense side)
 *   - Adjustments   = balance-adjustment rows (mark-to-market)
 *   - Other         = real income/expense flows that aren't transfer or
 *                     adjustment legs — the "Cash" view of activity.
 */
function filtersFor(accountId: string, tab: ActivityTab): TransactionFilters {
  switch (tab) {
    case 'Contributions':
      return { accountId, isTransfer: true, direction: 'Income' };
    case 'Withdrawals':
      return { accountId, isTransfer: true, direction: 'Expense' };
    case 'Adjustments':
      return { accountId, isAdjustment: true };
    case 'Other':
      return { accountId, isTransfer: false, isAdjustment: false };
    case 'All':
      return { accountId };
  }
}

interface Props {
  accountId: string;
  /** Account's opening balance in native currency (`AccountDetailDto.initialCapital`). */
  openingBalance?: number;
  /** ISO date the account was opened (`AccountDetailDto.openingDate`). */
  openingDate?: string;
  /** Native currency code of the account (`AccountDetailDto.currency`). */
  currency?: string;
}

/**
 * Activity section for the detail page. Rows are deletable here (the table is
 * rendered with `allowDelete`); edits still live on the /transactions route.
 * The sub-tabs are a thin preset on top of the existing TransactionsTable so
 * we don't fork any list-render logic.
 *
 * On the All tab we pin the account's opening balance as a display-only
 * summary row at the bottom of the list (including a zero opening balance, so
 * the account's starting point is always visible; skipped on the preset
 * sub-tabs, where an opening balance isn't a contribution/withdrawal/adjustment).
 */
export function ActivitySection({ accountId, openingBalance, openingDate, currency }: Props) {
  const [tab, setTab] = useState<ActivityTab>('All');
  const [page, setPage] = useState(1);
  const filters = filtersFor(accountId, tab);

  const showOpeningBalance =
    tab === 'All' &&
    openingBalance !== undefined &&
    openingDate !== undefined &&
    currency !== undefined;

  const openingBalanceRow = showOpeningBalance ? (
    <div
      className="flex items-center justify-between border-t-2 bg-muted/30 px-4 py-2 text-sm text-muted-foreground"
      data-testid="activity-opening-balance-row"
    >
      <span className="flex items-center gap-2">
        <span
          aria-hidden
          className="inline-block h-2 w-2 shrink-0 rounded-full bg-muted-foreground/50"
        />
        <span className="font-medium">Opening balance</span>
        <span>{formatShortDate(openingDate)}</span>
      </span>
      <span className="tabular-nums">{formatMoney(openingBalance, currency)}</span>
    </div>
  ) : undefined;

  return (
    <div className="space-y-3" data-testid="activity-section">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-base font-semibold tracking-tight">Activity</h2>
        <Tabs
          value={tab}
          onValueChange={(v) => {
            setTab(v as ActivityTab);
            setPage(1);
          }}
          aria-label="Activity filter"
        >
          <TabsList>
            <TabsTrigger value="All" data-testid="activity-tab-all">
              All
            </TabsTrigger>
            <TabsTrigger value="Contributions" data-testid="activity-tab-contributions">
              Contributions
            </TabsTrigger>
            <TabsTrigger value="Withdrawals" data-testid="activity-tab-withdrawals">
              Withdrawals
            </TabsTrigger>
            <TabsTrigger value="Adjustments" data-testid="activity-tab-adjustments">
              Adjustments
            </TabsTrigger>
            <TabsTrigger value="Other" data-testid="activity-tab-other">
              Other
            </TabsTrigger>
          </TabsList>
        </Tabs>
      </div>

      <TransactionsTable
        filters={filters}
        page={page}
        pageSize={ACTIVITY_PAGE_SIZE}
        onPageChange={setPage}
        allowDelete
        pinnedFooter={openingBalanceRow}
      />
    </div>
  );
}
