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
import { useState } from 'react';
import { AddParticipantDialog } from '@/src/components/pools/add-participant-dialog';
import { ArchivePoolDialog } from '@/src/components/pools/archive-pool-dialog';
import { CloseMonthDialog } from '@/src/components/pools/close-month-dialog';
import { RecordCostReimbursementDialog } from '@/src/components/pools/record-cost-reimbursement-dialog';
import { RecordRedemptionDialog } from '@/src/components/pools/record-redemption-dialog';
import { RecordSubscriptionDialog } from '@/src/components/pools/record-subscription-dialog';
import { Badge } from '@/src/components/ui/badge';
import { Button } from '@/src/components/ui/button';
import { formatShortDate } from '@/src/lib/utils/date';
import type { PoolDetailDto } from '@/src/types/api';

interface Props {
  pool: PoolDetailDto;
}

/**
 * Top strip of the detail page: back link, name, account link, currency and
 * archived badges, the inception subtitle, and the action group.
 *
 * Archived pools stay drillable — the loans and goals detail precedent — but
 * every mutating action disappears. There is no Unarchive button to put in
 * their place either: the backend has no such route, because archiving is only
 * permitted once nobody else holds a share, and re-opening would mean silently
 * re-taking a claim on an account that is wholly the user's again.
 *
 * All the money-moving dialogs live here rather than on the list page: each of
 * them needs the participant roster, which only the detail endpoint returns.
 */
export function PoolDetailHeader({ pool }: Props) {
  const [subscribeOpen, setSubscribeOpen] = useState(false);
  const [redeemOpen, setRedeemOpen] = useState(false);
  const [closeOpen, setCloseOpen] = useState(false);
  const [costOpen, setCostOpen] = useState(false);
  const [participantOpen, setParticipantOpen] = useState(false);
  const [archiveOpen, setArchiveOpen] = useState(false);

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
    </div>
  );
}
