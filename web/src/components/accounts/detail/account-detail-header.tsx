'use client';

import { ArrowLeft, ArrowLeftRight, Pencil } from 'lucide-react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { toast } from 'sonner';
import { DeleteAccountDialog } from '@/src/components/accounts/delete-account-dialog';
import { EditAccountDialog } from '@/src/components/accounts/edit-account-dialog';
import { UpdateBalanceDialog } from '@/src/components/accounts/update-balance-dialog';
import { CreateTransferDialog } from '@/src/components/transactions/create-transfer-dialog';
import { Badge } from '@/src/components/ui/badge';
import { Button } from '@/src/components/ui/button';
import { useArchiveAccount, useUnarchiveAccount } from '@/src/lib/api/accounts';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';
import type { AccountDetailDto, AccountDto, AccountType } from '@/src/types/api';

/**
 * Mirror of `accounts-table.tsx`'s ADJUSTABLE_TYPES — duplicated rather
 * than imported to keep the table component fully self-contained (the
 * detail page can ship without dragging the whole table module in).
 * If this list ever needs to change, update both call sites.
 */
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

interface Props {
  account: AccountDetailDto;
}

/**
 * Top strip of the detail page: back link, name, badges, balances, and
 * the action menu (button group, not dropdown). Update-balance is gated to
 * adjustable types only and opens the three-mode balance-change dialog
 * (investment / withdrawal / adjustment).
 */
export function AccountDetailHeader({ account }: Props) {
  // Shape the detail DTO into the lighter AccountDto that the two child
  // dialogs expect — they were designed around the list endpoint's shape,
  // and we don't want to leak the detail-only fields (initialCapital,
  // totals, ...) into their props.
  const accountForDialogs: AccountDto = {
    id: account.id,
    name: account.name,
    type: account.type,
    currency: account.currency,
    openingDate: account.openingDate,
    isArchived: account.isArchived,
    notes: account.notes,
    balance: account.balance,
    balanceMdl: account.balanceMdl,
  };

  const router = useRouter();
  const archive = useArchiveAccount();
  const unarchive = useUnarchiveAccount();
  const [adjustOpen, setAdjustOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [transferOpen, setTransferOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);

  const isAdjustable = ADJUSTABLE_TYPES.has(account.type);
  // "New transfer" with this account preselected as the source. Scoped to
  // CryptoExchange only for now (e.g. Bybit → Binance); destination can be any
  // non-archived account. Reuses the shared CreateTransferDialog, which hides
  // its own trigger when driven via the controlled `open` prop.
  const canTransferFrom = account.type === 'CryptoExchange';
  const showMdlEq = account.currency !== 'MDL';

  const handleArchive = async () => {
    try {
      await archive.mutateAsync(account.id);
      toast.success(`Archived "${account.name}"`);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to archive');
    }
  };

  const handleUnarchive = async () => {
    try {
      await unarchive.mutateAsync(account.id);
      toast.success(`Unarchived "${account.name}"`);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to unarchive');
    }
  };

  return (
    <div className="space-y-4" data-testid="account-detail-header">
      <div>
        <Link
          href="/accounts"
          aria-label="Back to accounts"
          className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
          data-testid="account-detail-back"
        >
          <ArrowLeft className="h-4 w-4" />
          Accounts
        </Link>
      </div>

      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-semibold tracking-tight" data-testid="account-detail-name">
              {account.name}
            </h1>
            <Badge variant="secondary" data-testid="account-detail-type">
              {TYPE_LABEL[account.type]}
            </Badge>
            <Badge variant="outline" data-testid="account-detail-currency">
              {account.currency}
            </Badge>
            {account.isArchived && (
              <Badge variant="outline" data-testid="account-detail-archived">
                Archived
              </Badge>
            )}
          </div>
          <p className="text-sm text-muted-foreground">
            Opened {formatShortDate(account.openingDate)}
          </p>
          <div className="flex flex-wrap items-baseline gap-2">
            <span
              className="text-xl font-semibold tabular-nums"
              data-testid="account-detail-balance"
            >
              {formatMoney(account.balance, account.currency)}
            </span>
            {showMdlEq && (
              <span
                className="text-sm text-muted-foreground tabular-nums"
                data-testid="account-detail-balance-mdl"
              >
                {account.balanceMdl !== null ? (
                  <>≈ {formatMoney(account.balanceMdl, 'MDL')}</>
                ) : (
                  <span title={`No FX rate available for ${account.currency}.`}>≈ —</span>
                )}
              </span>
            )}
          </div>
        </div>

        {!account.isArchived && (
          <div className="flex flex-wrap items-center gap-2" data-testid="account-detail-actions">
            <Button
              variant="outline"
              size="sm"
              onClick={() => setEditOpen(true)}
              data-testid="account-detail-edit"
            >
              <Pencil className="h-4 w-4" />
              Edit
            </Button>
            {canTransferFrom && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => setTransferOpen(true)}
                data-testid="account-detail-new-transfer"
              >
                <ArrowLeftRight className="h-4 w-4" />
                New transfer
              </Button>
            )}
            {isAdjustable && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => setAdjustOpen(true)}
                data-testid="account-detail-update-balance"
              >
                <Pencil className="h-4 w-4" />
                Update balance
              </Button>
            )}
            <Button
              variant="ghost"
              size="sm"
              onClick={handleArchive}
              disabled={archive.isPending}
              data-testid="account-detail-archive"
            >
              Archive
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setDeleteOpen(true)}
              className="text-destructive hover:text-destructive"
              data-testid="account-detail-delete"
            >
              Delete permanently
            </Button>
          </div>
        )}

        {account.isArchived && (
          <div className="flex flex-wrap items-center gap-2" data-testid="account-detail-actions">
            <Button
              variant="ghost"
              size="sm"
              onClick={handleUnarchive}
              disabled={unarchive.isPending}
              data-testid="account-detail-unarchive"
            >
              Unarchive
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setDeleteOpen(true)}
              className="text-destructive hover:text-destructive"
              data-testid="account-detail-delete"
            >
              Delete permanently
            </Button>
          </div>
        )}
      </div>

      <EditAccountDialog account={accountForDialogs} open={editOpen} onOpenChange={setEditOpen} />

      {isAdjustable && (
        <UpdateBalanceDialog
          account={accountForDialogs}
          open={adjustOpen}
          onOpenChange={setAdjustOpen}
        />
      )}

      {canTransferFrom && (
        <CreateTransferDialog
          open={transferOpen}
          onOpenChange={setTransferOpen}
          defaultSourceAccountId={account.id}
        />
      )}

      <DeleteAccountDialog
        account={{ id: account.id, name: account.name }}
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
        // The account no longer exists on success — leave the detail page.
        onDeleted={() => router.push('/accounts')}
      />
    </div>
  );
}
