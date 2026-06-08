'use client';

import { MoreHorizontal } from 'lucide-react';
import Link from 'next/link';
import { useState } from 'react';
import { toast } from 'sonner';
import { DeleteAccountDialog } from '@/src/components/accounts/delete-account-dialog';
import { UpdateBalanceDialog } from '@/src/components/accounts/update-balance-dialog';
import { Badge } from '@/src/components/ui/badge';
import { Button } from '@/src/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/src/components/ui/dropdown-menu';
import { Label } from '@/src/components/ui/label';
import { Switch } from '@/src/components/ui/switch';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/src/components/ui/table';
import { useAccounts, useArchiveAccount, useUnarchiveAccount } from '@/src/lib/api/accounts';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';
import type { AccountDto, AccountType } from '@/src/types/api';

// Account types where the user has no per-transaction ledger and the
// balance moves on its own (market gains, interest, etc). Only these expose
// "Update balance" — for Cash/CreditCard/BankCurrent you'd just add a
// regular transaction instead.
const ADJUSTABLE_TYPES: ReadonlySet<AccountType> = new Set([
  'Brokerage',
  'CryptoExchange',
  'P2PLending',
  'BankDeposit',
]);

const TYPE_LABEL: Record<AccountType, string> = {
  Cash: 'Cash',
  CreditCard: 'Credit card',
  BankDeposit: 'Bank deposit',
  BankCurrent: 'Bank current',
  Brokerage: 'Brokerage',
  CryptoExchange: 'Crypto exchange',
  P2PLending: 'P2P lending',
};

export function AccountsTable() {
  const [includeArchived, setIncludeArchived] = useState(false);
  const [adjustTarget, setAdjustTarget] = useState<AccountDto | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<AccountDto | null>(null);
  const { data, isLoading, isError } = useAccounts(includeArchived);
  const archive = useArchiveAccount();
  const unarchive = useUnarchiveAccount();

  const handleArchive = async (id: string, name: string) => {
    try {
      await archive.mutateAsync(id);
      toast.success(`Archived "${name}"`);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to archive');
    }
  };

  const handleUnarchive = async (id: string, name: string) => {
    try {
      await unarchive.mutateAsync(id);
      toast.success(`Unarchived "${name}"`);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to unarchive');
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-end gap-2">
        <Label htmlFor="show-archived" className="text-sm text-muted-foreground">
          Show archived
        </Label>
        <Switch
          id="show-archived"
          data-testid="show-archived-toggle"
          checked={includeArchived}
          onCheckedChange={setIncludeArchived}
        />
      </div>

      <div className="rounded-lg border">
        <Table data-testid="accounts-table">
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Type</TableHead>
              <TableHead className="text-right">Balance</TableHead>
              <TableHead className="text-right">MDL eq.</TableHead>
              <TableHead>Opening date</TableHead>
              <TableHead>Status</TableHead>
              <TableHead className="w-12 text-right">
                <span className="sr-only">Actions</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isError ? (
              <TableRow>
                <TableCell colSpan={7} className="text-center text-destructive">
                  Failed to load accounts.
                </TableCell>
              </TableRow>
            ) : isLoading || !data ? (
              <TableRow>
                <TableCell colSpan={7} className="text-center text-muted-foreground">
                  Loading...
                </TableCell>
              </TableRow>
            ) : data.length === 0 ? (
              <TableRow>
                <TableCell colSpan={7} className="text-center text-muted-foreground">
                  No accounts yet. Click &ldquo;Add account&rdquo; to start.
                </TableCell>
              </TableRow>
            ) : (
              data.map((account) => (
                <TableRow key={account.id} data-testid="account-row">
                  <TableCell className="font-medium">
                    {/* Only the name cell is a link — keeps the row-action
                        dropdown's pointer events isolated so clicking the
                        menu trigger never navigates. */}
                    <Link
                      href={`/accounts/${account.id}`}
                      className="rounded-sm underline-offset-4 hover:text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
                      data-testid="account-name-link"
                    >
                      {account.name}
                    </Link>
                  </TableCell>
                  <TableCell>
                    <Badge variant="secondary">{TYPE_LABEL[account.type]}</Badge>
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {formatMoney(account.balance, account.currency)}
                  </TableCell>
                  <TableCell
                    className="text-right tabular-nums text-muted-foreground"
                    data-testid="account-mdl-eq"
                  >
                    {account.balanceMdl !== null ? (
                      formatMoney(account.balanceMdl, 'MDL')
                    ) : (
                      <span
                        title={`No FX rate available for ${account.currency} on ${account.openingDate}. Add one in Settings → FX rates.`}
                      >
                        —
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {formatShortDate(account.openingDate)}
                  </TableCell>
                  <TableCell>
                    {account.isArchived ? (
                      <Badge variant="outline">Archived</Badge>
                    ) : (
                      <Badge variant="success">Active</Badge>
                    )}
                  </TableCell>
                  <TableCell className="text-right">
                    {account.isArchived ? (
                      <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                          <Button
                            variant="ghost"
                            size="icon"
                            aria-label={`Actions for ${account.name}`}
                            data-testid="account-actions"
                          >
                            <MoreHorizontal className="h-4 w-4" />
                          </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          <DropdownMenuItem
                            onClick={() => handleUnarchive(account.id, account.name)}
                            data-testid="unarchive-account"
                          >
                            Unarchive
                          </DropdownMenuItem>
                          <DropdownMenuItem
                            onClick={() => setDeleteTarget(account)}
                            data-testid="delete-account"
                            className="text-destructive focus:text-destructive"
                          >
                            Delete permanently
                          </DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    ) : (
                      <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                          <Button
                            variant="ghost"
                            size="icon"
                            aria-label={`Actions for ${account.name}`}
                            data-testid="account-actions"
                          >
                            <MoreHorizontal className="h-4 w-4" />
                          </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          {ADJUSTABLE_TYPES.has(account.type) && (
                            <DropdownMenuItem
                              onClick={() => setAdjustTarget(account)}
                              data-testid="update-balance-action"
                            >
                              Update balance
                            </DropdownMenuItem>
                          )}
                          <DropdownMenuItem
                            onClick={() => handleArchive(account.id, account.name)}
                            data-testid="archive-account"
                          >
                            Archive
                          </DropdownMenuItem>
                          <DropdownMenuItem
                            onClick={() => setDeleteTarget(account)}
                            data-testid="delete-account"
                            className="text-destructive focus:text-destructive"
                          >
                            Delete permanently
                          </DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    )}
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>

      {adjustTarget && (
        <UpdateBalanceDialog
          account={adjustTarget}
          open={adjustTarget !== null}
          onOpenChange={(next) => {
            if (!next) setAdjustTarget(null);
          }}
        />
      )}

      {deleteTarget && (
        <DeleteAccountDialog
          account={deleteTarget}
          open={deleteTarget !== null}
          onOpenChange={(next) => {
            if (!next) setDeleteTarget(null);
          }}
        />
      )}
    </div>
  );
}
