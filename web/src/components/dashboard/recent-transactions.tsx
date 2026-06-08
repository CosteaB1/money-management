'use client';

import { Badge } from '@/src/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/src/components/ui/table';
import { useAccounts } from '@/src/lib/api/accounts';
import { useTransactions } from '@/src/lib/api/transactions';
import { cn } from '@/src/lib/utils/cn';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';

const RECENT_LIMIT = 10;

export function RecentTransactions() {
  const { data, isLoading } = useTransactions({}, 1, RECENT_LIMIT);
  const accountsQuery = useAccounts(true);

  const accountById = new Map(accountsQuery.data?.map((a) => [a.id, a] as const) ?? []);
  const recent = data?.items ?? [];

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-sm font-medium text-muted-foreground">
          Recent transactions
        </CardTitle>
      </CardHeader>
      <CardContent>
        {isLoading || !data ? (
          <div className="h-32 animate-pulse rounded bg-muted" />
        ) : recent.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">No transactions yet.</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Date</TableHead>
                <TableHead>Description</TableHead>
                <TableHead>Category</TableHead>
                <TableHead>Account</TableHead>
                <TableHead className="text-right">Amount</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {recent.map((t) => {
                const isExpense = t.direction === 'Expense';
                const isTransfer = t.isTransfer;
                const isAdjustment = t.isAdjustment;
                const isInternal = isTransfer || isAdjustment;
                // Mirror the transactions-page table: transfers and adjustments
                // are visually muted because they're internal movements, not
                // real income/expense.
                const amountClass = isInternal
                  ? 'text-muted-foreground'
                  : isExpense
                    ? 'text-destructive'
                    : 'text-emerald-500';
                const sign = isExpense ? '-' : '+';
                const account = accountById.get(t.accountId);
                const currency = t.currency || account?.currency || 'MDL';
                const showMdlEq = currency !== 'MDL';
                return (
                  <TableRow
                    key={t.id}
                    data-transfer={isTransfer ? 'true' : 'false'}
                    data-adjustment={isAdjustment ? 'true' : 'false'}
                  >
                    <TableCell className="text-muted-foreground">
                      {formatShortDate(t.transactionDate)}
                    </TableCell>
                    <TableCell className="font-medium">
                      <div className="flex items-center gap-2">
                        <span>{t.description}</span>
                        {isTransfer && <Badge variant="outline">Transfer</Badge>}
                        {isAdjustment && <Badge variant="secondary">Balance adjustment</Badge>}
                      </div>
                    </TableCell>
                    <TableCell>
                      {t.categoryName ? (
                        <span className="text-muted-foreground">{t.categoryName}</span>
                      ) : (
                        <Badge variant="outline">Uncategorized</Badge>
                      )}
                    </TableCell>
                    <TableCell className="text-muted-foreground">{account?.name ?? '—'}</TableCell>
                    <TableCell className={cn('text-right tabular-nums font-medium', amountClass)}>
                      <div>
                        {sign}
                        {formatMoney(t.amount, currency)}
                      </div>
                      {showMdlEq && (
                        <div className="text-xs font-normal text-muted-foreground">
                          {t.amountMdl !== null ? (
                            <>
                              {sign}
                              {formatMoney(t.amountMdl, 'MDL')}
                            </>
                          ) : (
                            <span
                              title={`No FX rate available for ${currency} on ${t.transactionDate}.`}
                            >
                              —
                            </span>
                          )}
                        </div>
                      )}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}
