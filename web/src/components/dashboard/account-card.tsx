'use client';

import {
  Bitcoin,
  Building2,
  CreditCard,
  HandCoins,
  PiggyBank,
  TrendingUp,
  Wallet,
} from 'lucide-react';
import { Badge } from '@/src/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';
import { useAccounts } from '@/src/lib/api/accounts';
import { formatMoney } from '@/src/lib/utils/currency';
import type { AccountType } from '@/src/types/api';

const TYPE_META: Record<
  AccountType,
  { label: string; icon: React.ComponentType<{ className?: string }> }
> = {
  Cash: { label: 'Cash', icon: Wallet },
  CreditCard: { label: 'Credit Card', icon: CreditCard },
  BankDeposit: { label: 'Bank Deposit', icon: PiggyBank },
  BankCurrent: { label: 'Bank Current', icon: Building2 },
  Brokerage: { label: 'Brokerage', icon: TrendingUp },
  CryptoExchange: { label: 'Crypto Exchange', icon: Bitcoin },
  P2PLending: { label: 'P2P Lending', icon: HandCoins },
};

export function AccountCards() {
  const { data, isLoading } = useAccounts(false);

  return (
    <Card className="h-full">
      <CardHeader>
        <CardTitle className="text-sm font-medium text-muted-foreground">Accounts</CardTitle>
      </CardHeader>
      <CardContent className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        {isLoading || !data ? (
          <div
            className="col-span-full h-20 animate-pulse rounded bg-muted"
            role="status"
            aria-label="Loading accounts"
          />
        ) : (
          data.map((account) => {
            const meta = TYPE_META[account.type];
            const Icon = meta.icon;
            return (
              <div
                key={account.id}
                className="rounded-lg border border-border p-4"
                data-testid="account-summary"
              >
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <Icon className="h-4 w-4 text-muted-foreground" aria-hidden />
                    <span className="text-sm font-medium">{account.name}</span>
                  </div>
                  <Badge variant="secondary">{meta.label}</Badge>
                </div>
                <p className="mt-3 text-xl font-semibold tabular-nums">
                  {formatMoney(account.balance, account.currency)}
                </p>
                {account.currency !== 'MDL' && (
                  <p className="mt-1 text-xs text-muted-foreground tabular-nums">
                    {account.balanceMdl !== null
                      ? `≈ ${formatMoney(account.balanceMdl, 'MDL')}`
                      : 'MDL rate missing'}
                  </p>
                )}
              </div>
            );
          })
        )}
      </CardContent>
    </Card>
  );
}
