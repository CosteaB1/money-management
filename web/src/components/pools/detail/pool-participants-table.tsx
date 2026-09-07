'use client';

import { AlertTriangle } from 'lucide-react';
import { Badge } from '@/src/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/src/components/ui/table';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatSharePercent } from '@/src/lib/utils/pool';
import type { PoolDetailDto, PoolParticipantDto } from '@/src/types/api';

interface Props {
  pool: PoolDetailDto;
}

/**
 * The roster, in the vocabulary the design review insisted on: **share** and
 * **percentages**, never "units". Unit counts are audit data and live in the
 * ledger below.
 *
 * The column that needs the most care is **Payable now**. It is
 * `max(0, stake − whatTheyPutIn)`, which is the high-water mark with no stored
 * field: below their basis it is zero, so a *green* month after a drawdown
 * correctly pays nothing. That is the single most disputable behaviour in the
 * whole design — somebody watching the pool go up and getting no payout will
 * assume the app is broken — so a zero is never rendered as a bare "0.00". It
 * says what it means.
 *
 * The owner's row is muted for the opposite reason: a seed carries no cash, so
 * the owner's basis is 0 and the formula reports their entire stake as
 * "payable". That is arithmetic, not an offer. The owner's profit stays in and
 * grows their share; they take value out with a withdrawal.
 */
export function PoolParticipantsTable({ pool }: Props) {
  return (
    <div className="space-y-3" data-testid="pool-participants-section">
      <h2 className="text-base font-semibold tracking-tight">Who holds what</h2>
      <div className="rounded-lg border">
        <Table data-testid="pool-participants-table">
          <TableHeader>
            <TableRow>
              <TableHead>Participant</TableHead>
              <TableHead className="text-right">Share</TableHead>
              <TableHead className="text-right">Worth</TableHead>
              <TableHead className="text-right">Put in</TableHead>
              <TableHead className="text-right">Payable now</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {pool.participants.map((participant) => (
              <ParticipantRow key={participant.id} participant={participant} pool={pool} />
            ))}
          </TableBody>
        </Table>
      </div>
      <p className="text-xs text-muted-foreground" data-testid="pool-participants-footnote">
        <strong>Payable now</strong> is profit above what someone put in. It is zero while they are
        at or below that figure — so a month the pool went up can still pay them nothing, and that
        is correct: they are only made whole first. Your own row is shown for completeness; your
        profit stays in the pool and grows your share instead of being paid out.
      </p>
    </div>
  );
}

function ParticipantRow({
  participant,
  pool,
}: {
  participant: PoolParticipantDto;
  pool: PoolDetailDto;
}) {
  const distributable = participant.distributable;
  const nothingPayable = distributable !== null && distributable <= 0;

  return (
    <TableRow
      data-testid="pool-participant-row"
      data-owner={participant.isOwner ? 'true' : 'false'}
      data-archived={participant.isArchived ? 'true' : 'false'}
    >
      <TableCell className="font-medium">
        <span className="inline-flex flex-wrap items-center gap-1.5">
          <span data-testid="pool-participant-name">{participant.name}</span>
          {participant.isOwner && (
            <Badge variant="secondary" data-testid="pool-participant-owner-badge">
              You
            </Badge>
          )}
          {participant.isArchived && (
            <Badge variant="outline" data-testid="pool-participant-archived-badge">
              Archived
            </Badge>
          )}
        </span>
      </TableCell>
      <TableCell className="text-right tabular-nums" data-testid="pool-participant-share">
        {formatSharePercent(participant.ownershipPercent)}
      </TableCell>
      <TableCell className="text-right tabular-nums">
        <span
          className="inline-flex items-center justify-end gap-1.5"
          data-testid="pool-participant-stake"
        >
          {participant.stake !== null ? (
            formatMoney(participant.stake, pool.currency)
          ) : (
            <span title="No shares outstanding, so there is no price to value this at.">—</span>
          )}
          {participant.missingFxRate && (
            <span
              title={`No FX rate available to convert ${pool.currency} to MDL`}
              role="img"
              aria-label="No FX rate available to convert to MDL"
              data-testid="pool-participant-missing-fx"
              className="inline-flex"
            >
              <AlertTriangle className="h-3.5 w-3.5 text-amber-500" aria-hidden />
            </span>
          )}
        </span>
        {pool.currency !== 'MDL' && participant.stakeMdl !== null && !participant.missingFxRate && (
          <div
            className="text-xs font-normal text-muted-foreground"
            data-testid="pool-participant-stake-mdl"
          >
            {formatMoney(participant.stakeMdl, 'MDL')}
          </div>
        )}
      </TableCell>
      <TableCell
        className="text-right tabular-nums text-muted-foreground"
        data-testid="pool-participant-capital-base"
      >
        {formatMoney(participant.capitalBase, pool.currency)}
      </TableCell>
      <TableCell className="text-right">
        {distributable === null ? (
          <span
            className="text-muted-foreground"
            title="No price per share, so nothing can be valued."
          >
            —
          </span>
        ) : participant.isOwner ? (
          <span
            className="text-xs text-muted-foreground"
            data-testid="pool-participant-distributable-owner"
            title="Your profit stays in the pool and grows your share. Take value out with a withdrawal instead."
          >
            Stays in the pool
          </span>
        ) : nothingPayable ? (
          <span
            className="text-xs text-muted-foreground"
            data-testid="pool-participant-distributable-zero"
          >
            Nothing payable — still at or below what they put in
          </span>
        ) : (
          <span className="font-medium tabular-nums" data-testid="pool-participant-distributable">
            {formatMoney(distributable, pool.currency)}
          </span>
        )}
      </TableCell>
    </TableRow>
  );
}
