'use client';

import {
  ArrowDownLeft,
  ArrowLeft,
  ArrowUpRight,
  CalendarCheck,
  Receipt,
  UserPlus,
} from 'lucide-react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { toast } from 'sonner';
import { AddParticipantDialog } from '@/src/components/pools/add-participant-dialog';
import { ArchivePoolDialog } from '@/src/components/pools/archive-pool-dialog';
import { CloseMonthDialog } from '@/src/components/pools/close-month-dialog';
import { DeletePoolDialog } from '@/src/components/pools/delete-pool-dialog';
import { RecordCostReimbursementDialog } from '@/src/components/pools/record-cost-reimbursement-dialog';
import { RecordRedemptionDialog } from '@/src/components/pools/record-redemption-dialog';
import { RecordSubscriptionDialog } from '@/src/components/pools/record-subscription-dialog';
import { Badge } from '@/src/components/ui/badge';
import { Button } from '@/src/components/ui/button';
import { useUnarchivePool } from '@/src/lib/api/pools';
import { formatShortDate } from '@/src/lib/utils/date';
import type { PoolDetailDto } from '@/src/types/api';

interface Props {
  pool: PoolDetailDto;
}

/**
 * Top strip of the detail page: back link, name, account link, currency and
 * archived badges, the inception subtitle, and the action group.
 *
 * Archived pools stay drillable — the loans and goals detail precedent — and
 * every money-moving action disappears while archived, replaced by the two
 * lifecycle actions that still make sense: **Unarchive** (put it back in
 * service) and **Delete** (it should never have existed). Mirrors the loan and
 * account detail headers.
 *
 * All the money-moving dialogs live here rather than on the list page: each of
 * them needs the participant roster, which only the detail endpoint returns.
 */
export function PoolDetailHeader({ pool }: Props) {
  const router = useRouter();
  const unarchive = useUnarchivePool();
  const [subscribeOpen, setSubscribeOpen] = useState(false);
  const [redeemOpen, setRedeemOpen] = useState(false);
  const [closeOpen, setCloseOpen] = useState(false);
  const [costOpen, setCostOpen] = useState(false);
  const [participantOpen, setParticipantOpen] = useState(false);
  const [archiveOpen, setArchiveOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);

  const handleUnarchive = async () => {
    try {
      await unarchive.mutateAsync(pool.id);
      toast.success(`Unarchived "${pool.name}"`);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to unarchive pool');
    }
  };

  return (
    <div className="space-y-4" data-testid="pool-detail-header">
      <div>
        <Link
          href="/pools"
          aria-label="Back to pools"
          className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
          data-testid="pool-detail-back"
        >
          <ArrowLeft className="h-4 w-4" />
          Pools
        </Link>
      </div>

      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-semibold tracking-tight" data-testid="pool-detail-name">
              {pool.name}
            </h1>
            <Badge variant="outline" data-testid="pool-detail-currency">
              {pool.currency}
            </Badge>
            {pool.isArchived && (
              <Badge variant="outline" data-testid="pool-detail-archived">
                Archived
              </Badge>
            )}
          </div>
          <p className="text-sm text-muted-foreground" data-testid="pool-detail-subtitle">
            Held in{' '}
            <Link
              href={`/accounts/${pool.accountId}`}
              className="font-medium text-foreground underline-offset-4 hover:underline"
              data-testid="pool-detail-account-link"
            >
              {pool.accountName}
            </Link>{' '}
            · started {formatShortDate(pool.inceptionDate)}
          </p>
          {/* Unarchiving is not a cosmetic flip: an active pool re-arms every
              guard on the account, so say what comes back before the button is
              pressed rather than after the next refusal. */}
          {pool.isArchived && (
            <p className="text-sm text-muted-foreground" data-testid="pool-detail-unarchive-note">
              Unarchiving puts this pool back in service and re-arms the guards on{' '}
              <strong>{pool.accountName}</strong>: manual transactions, transfers, imports and loan
              movements on that account are blocked again, and every movement has to go through the
              pool.
            </p>
          )}
        </div>

        {!pool.isArchived && (
          <div className="flex flex-wrap items-center gap-2" data-testid="pool-detail-actions">
            <Button
              variant="outline"
              size="sm"
              onClick={() => setSubscribeOpen(true)}
              data-testid="pool-detail-record-subscription"
            >
              <ArrowDownLeft className="h-4 w-4" />
              Money in
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setRedeemOpen(true)}
              data-testid="pool-detail-record-redemption"
            >
              <ArrowUpRight className="h-4 w-4" />
              Money out
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setCloseOpen(true)}
              data-testid="pool-detail-close-month"
            >
              <CalendarCheck className="h-4 w-4" />
              Close month
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setCostOpen(true)}
              data-testid="pool-detail-cost"
            >
              <Receipt className="h-4 w-4" />
              Shared cost
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setParticipantOpen(true)}
              data-testid="pool-detail-add-participant"
            >
              <UserPlus className="h-4 w-4" />
              Add participant
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setArchiveOpen(true)}
              data-testid="pool-detail-archive"
            >
              Archive
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setDeleteOpen(true)}
              className="text-destructive hover:text-destructive"
              data-testid="pool-detail-delete"
            >
              Delete
            </Button>
          </div>
        )}

        {/* An archived pool swaps the whole group for the two lifecycle
            actions — mirrors the loan and account detail headers. */}
        {pool.isArchived && (
          <div className="flex flex-wrap items-center gap-2" data-testid="pool-detail-actions">
            <Button
              variant="ghost"
              size="sm"
              onClick={handleUnarchive}
              disabled={unarchive.isPending}
              data-testid="pool-detail-unarchive"
            >
              Unarchive
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setDeleteOpen(true)}
              className="text-destructive hover:text-destructive"
              data-testid="pool-detail-delete"
            >
              Delete
            </Button>
          </div>
        )}
      </div>

      {!pool.isArchived && (
        <>
          <RecordSubscriptionDialog
            pool={pool}
            open={subscribeOpen}
            onOpenChange={setSubscribeOpen}
          />
          <RecordRedemptionDialog pool={pool} open={redeemOpen} onOpenChange={setRedeemOpen} />
          <CloseMonthDialog pool={pool} open={closeOpen} onOpenChange={setCloseOpen} />
          <RecordCostReimbursementDialog pool={pool} open={costOpen} onOpenChange={setCostOpen} />
          <AddParticipantDialog
            poolId={pool.id}
            poolName={pool.name}
            open={participantOpen}
            onOpenChange={setParticipantOpen}
          />
          <ArchivePoolDialog
            pool={{
              id: pool.id,
              name: pool.name,
              currency: pool.currency,
              outsideCapital: pool.outsideCapital,
            }}
            open={archiveOpen}
            onOpenChange={setArchiveOpen}
          />
        </>
      )}

      {/* Reachable in both states — an archived pool is precisely the one that
          gets stuck, since it goes on holding its account through the
          restricting FK. */}
      <DeletePoolDialog
        pool={{
          id: pool.id,
          name: pool.name,
          accountName: pool.accountName,
          currency: pool.currency,
          outsideCapital: pool.outsideCapital,
          isArchived: pool.isArchived,
        }}
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
        // The pool no longer exists on success — this page is now dead.
        onDeleted={() => router.push('/pools')}
        onArchiveInstead={() => setArchiveOpen(true)}
      />
    </div>
  );
}
