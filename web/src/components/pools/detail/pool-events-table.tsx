'use client';

import { Trash2 } from 'lucide-react';
import { useState } from 'react';
import { DeleteEventDialog } from '@/src/components/pools/delete-event-dialog';
import { SettlePayoutDialog } from '@/src/components/pools/settle-payout-dialog';
import { Badge } from '@/src/components/ui/badge';
import { Button } from '@/src/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/src/components/ui/table';
import { formatMoney } from '@/src/lib/utils/currency';
import { formatShortDate } from '@/src/lib/utils/date';
import { formatNav, formatUnits, POOL_EVENT_LABEL } from '@/src/lib/utils/pool';
import type { PoolDetailDto, PoolUnitEventDto } from '@/src/types/api';

interface Props {
  pool: PoolDetailDto;
}

/**
 * The full ledger, newest first (the server orders it).
 *
 * **This is the only place in the UI that shows share counts and prices per
 * share**, and that is a deliberate boundary. Everywhere else the app speaks
 * in shares-as-percentages and money, because "units" is fund-accounting
 * vocabulary that means nothing to two friends with a thousand dollars each.
 * An audit trail is the one context where the raw numbers earn their place:
 * this table is what you reconstruct a disputed month from.
 *
 * The **Unpaid** badge is the load-bearing status here. A closed payout is a
 * debt that is still sitting in the account — the pool's value already
 * excludes it, but the cash has not moved, and it stays that way until Settle
 * records the day it actually left.
 */
export function PoolEventsTable({ pool }: Props) {
  const [settleTarget, setSettleTarget] = useState<PoolUnitEventDto | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<PoolUnitEventDto | null>(null);

  return (
    <div className="space-y-3" data-testid="pool-events-section">
      <div className="space-y-1">
        <h2 className="text-base font-semibold tracking-tight">Ledger</h2>
        <p className="text-xs text-muted-foreground">
          Every movement, newest first. Share counts appear here and nowhere else — this is the
          audit trail.
        </p>
      </div>
      <div className="rounded-lg border">
        <Table data-testid="pool-events-table">
          <TableHeader>
            <TableRow>
              <TableHead>Date</TableHead>
              <TableHead>What happened</TableHead>
              <TableHead>Who</TableHead>
              <TableHead className="text-right">Cash</TableHead>
              <TableHead className="text-right">Price per share</TableHead>
              <TableHead className="text-right">Shares</TableHead>
              <TableHead className="w-24 text-right">
                <span className="sr-only">Actions</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {pool.events.length === 0 ? (
              <TableRow>
                <TableCell
                  colSpan={7}
                  className="py-10 text-center text-muted-foreground"
                  data-testid="pool-events-empty"
                >
                  Nothing recorded yet.
                </TableCell>
              </TableRow>
            ) : (
              pool.events.map((event) => (
                <TableRow key={event.id} data-testid="pool-event-row" data-kind={event.kind}>
                  <TableCell className="text-muted-foreground" data-testid="pool-event-date">
                    {formatShortDate(event.occurredOn)}
                  </TableCell>
                  <TableCell>
                    <span className="inline-flex flex-wrap items-center gap-1.5">
                      <span data-testid="pool-event-kind">{POOL_EVENT_LABEL[event.kind]}</span>
                      {event.isUnpaid && (
                        <Badge variant="warning" data-testid="pool-event-unpaid-badge">
                          Unpaid
                        </Badge>
                      )}
                    </span>
                  </TableCell>
                  <TableCell data-testid="pool-event-participant">
                    {event.participantName}
                  </TableCell>
                  <TableCell className="text-right tabular-nums" data-testid="pool-event-cash">
                    {event.cash !== null ? (
                      formatMoney(event.cash, event.cashCurrency ?? pool.currency)
                    ) : (
                      <span
                        className="text-muted-foreground"
                        title="Shares moved without any cash — the account is untouched."
                      >
                        —
                      </span>
                    )}
                  </TableCell>
                  <TableCell
                    className="text-right tabular-nums text-muted-foreground"
                    data-testid="pool-event-nav"
                  >
                    {formatNav(event.navPerUnit)}
                  </TableCell>
                  <TableCell
                    className="text-right tabular-nums text-muted-foreground"
                    data-testid="pool-event-units"
                  >
                    {event.unitsDelta < 0 ? '−' : '+'}
                    {formatUnits(Math.abs(event.unitsDelta))}
                  </TableCell>
                  <TableCell className="text-right">
                    {!pool.isArchived && (
                      <span className="inline-flex items-center justify-end gap-1">
                        {event.isUnpaid && (
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            onClick={() => setSettleTarget(event)}
                            data-testid="pool-event-settle"
                          >
                            Settle
                          </Button>
                        )}
                        {/* The seed cannot be deleted — the backend refuses it,
                            because a pool with no opening stake has no shares to
                            price anything against. */}
                        {event.kind !== 'Seed' && (
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            className="h-8 w-8 text-muted-foreground hover:text-destructive"
                            aria-label={`Delete ${POOL_EVENT_LABEL[event.kind].toLowerCase()} for ${event.participantName}`}
                            data-testid="pool-event-delete"
                            onClick={() => setDeleteTarget(event)}
                          >
                            <Trash2 aria-hidden />
                          </Button>
                        )}
                      </span>
                    )}
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>

      {settleTarget && (
        <SettlePayoutDialog
          poolId={pool.id}
          poolCurrency={pool.currency}
          accountName={pool.accountName}
          event={settleTarget}
          open={settleTarget !== null}
          onOpenChange={(next) => {
            if (!next) setSettleTarget(null);
          }}
        />
      )}
      {deleteTarget && (
        <DeleteEventDialog
          poolId={pool.id}
          poolCurrency={pool.currency}
          event={deleteTarget}
          open={deleteTarget !== null}
          onOpenChange={(next) => {
            if (!next) setDeleteTarget(null);
          }}
        />
      )}
    </div>
  );
}
