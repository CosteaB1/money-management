'use client';

import { ShieldAlert } from 'lucide-react';
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
import { formatUnits, POOL_EVENT_LABEL } from '@/src/lib/utils/pool';
import type { PoolDetailDto } from '@/src/types/api';

interface Props {
  pool: PoolDetailDto;
}

/**
 * The tripwire. **It reports; it never corrects.**
 *
 * Every finding here is recoverable by hand and unrecoverable if silently
 * "fixed" — a value drift means somebody edited history after shares were
 * priced against it, and which of the two records is wrong depends entirely on
 * what actually happened. So the copy tells the user to go and look, and is
 * careful never to imply the app is about to sort it out.
 *
 * Four classes, and each one means something specific:
 *
 * 1. **Money with no entry behind it** — cash moved on the account without a
 *    matching ledger entry. It is being shared pro-rata with the other
 *    participants right now, whoever it belonged to.
 * 2. **Share count disagrees** — the roster's total and the ledger's total have
 *    parted company. Only assertable at all because the owner holds real
 *    shares rather than "whatever is left over".
 * 3. **Priced against a value the ledger cannot reproduce** — a row dated on or
 *    before a priced entry was added, moved or deleted after the fact.
 * 4. **Entries claiming money the account never saw** — the mirror of (1). This
 *    is the only check that catches a backfilled arrival that never happened.
 *
 * Renders nothing when everything reconciles: a permanently green panel is
 * noise, and noise is what gets ignored on the day it turns red.
 */
export function PoolReconciliationPanel({ pool }: Props) {
  const r = pool.reconciliation;
  if (r.isClean) return null;

  return (
    <section
      className="space-y-4 rounded-lg border border-destructive/50 bg-destructive/5 p-4"
      data-testid="pool-reconciliation-panel"
      aria-labelledby="pool-reconciliation-heading"
    >
      <div className="flex items-start gap-2">
        <ShieldAlert className="mt-0.5 size-5 shrink-0 text-destructive" aria-hidden />
        <div className="space-y-1">
          <h2 id="pool-reconciliation-heading" className="text-base font-semibold text-destructive">
            This pool&rsquo;s ledger doesn&rsquo;t match the account
          </h2>
          <p className="text-sm text-muted-foreground">
            Nothing below has been changed or repaired, and nothing will be —{' '}
            <strong>this is a report</strong>. Each finding has a different right answer depending
            on what actually happened, and guessing would move real money between you and the other
            participants. Investigate before recording anything else in this pool.
          </p>
        </div>
      </div>

      {r.unmatchedTransactions.length > 0 && (
        <Finding
          title="Money on the account with no ledger entry behind it"
          body="These moved the account's balance without minting or retiring any shares, so every one of them is being split pro-rata with the other participants — whoever the money actually belonged to."
          testId="reconciliation-unmatched"
        >
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Date</TableHead>
                <TableHead>Description</TableHead>
                <TableHead className="text-right">Amount</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {r.unmatchedTransactions.map((t) => (
                <TableRow key={t.transactionId} data-testid="reconciliation-unmatched-row">
                  <TableCell className="text-muted-foreground">
                    {formatShortDate(t.transactionDate)}
                  </TableCell>
                  <TableCell>{t.description}</TableCell>
                  <TableCell className="text-right tabular-nums">
                    {t.direction === 'Expense' ? '−' : '+'}
                    {formatMoney(t.amount, t.currency)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Finding>
      )}

      {!r.unitsBalance && (
        <Finding
          title="The share count doesn't add up"
          body="The shares recorded against participants and the shares the ledger accounts for have parted company. Until they agree, every percentage and every value on this page is derived from a total that is wrong."
          testId="reconciliation-units-drift"
        >
          <dl className="grid gap-2 text-sm sm:grid-cols-3">
            <div>
              <dt className="text-xs text-muted-foreground">Held by participants</dt>
              <dd className="tabular-nums" data-testid="reconciliation-participant-units">
                {formatUnits(r.participantUnits)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">Accounted for by the ledger</dt>
              <dd className="tabular-nums" data-testid="reconciliation-ledger-units">
                {formatUnits(r.ledgerUnits)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">Difference</dt>
              <dd
                className="font-medium tabular-nums"
                data-testid="reconciliation-units-drift-value"
              >
                {formatUnits(r.unitsDrift)}
              </dd>
            </div>
          </dl>
        </Finding>
      )}

      {r.valueDrifts.length > 0 && (
        <Finding
          title="Priced against a value the ledger can no longer reproduce"
          body="Each of these entries recorded the pool's value at the time it was priced, and replaying the account no longer produces that figure — a transaction dated on or before it was added, moved or deleted afterwards. The shares issued at that price are now wrong in someone's favour."
          testId="reconciliation-value-drift"
        >
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Date</TableHead>
                <TableHead>Entry</TableHead>
                <TableHead className="text-right">Recorded</TableHead>
                <TableHead className="text-right">Replayed</TableHead>
                <TableHead className="text-right">Difference</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {r.valueDrifts.map((d) => (
                <TableRow key={d.eventId} data-testid="reconciliation-value-drift-row">
                  <TableCell className="text-muted-foreground">
                    {formatShortDate(d.occurredOn)}
                  </TableCell>
                  <TableCell>{POOL_EVENT_LABEL[d.kind]}</TableCell>
                  <TableCell className="text-right tabular-nums">
                    {formatMoney(d.recordedPreMoney, pool.currency)}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {formatMoney(d.derivedPreMoney, pool.currency)}
                  </TableCell>
                  <TableCell className="text-right font-medium tabular-nums">
                    {formatMoney(d.drift, pool.currency)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Finding>
      )}

      {r.unbackedCashClaims.length > 0 && (
        <Finding
          title="Ledger entries claiming money the account never saw"
          body="These say cash moved on a day the account has no matching row for. Shares were minted or retired against money that never landed, which misprices everyone — and it is the only signal that catches an arrival backfilled by mistake."
          testId="reconciliation-unbacked"
        >
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Claimed date</TableHead>
                <TableHead>Direction</TableHead>
                <TableHead className="text-right">Amount</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {r.unbackedCashClaims.map((c) => (
                <TableRow key={c.eventId} data-testid="reconciliation-unbacked-row">
                  <TableCell className="text-muted-foreground">
                    {formatShortDate(c.settledOn)}
                  </TableCell>
                  <TableCell>{c.direction === 'Expense' ? 'Money out' : 'Money in'}</TableCell>
                  <TableCell className="text-right tabular-nums">
                    {formatMoney(c.amount, pool.currency)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Finding>
      )}
    </section>
  );
}

function Finding({
  title,
  body,
  testId,
  children,
}: {
  title: string;
  body: string;
  testId: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-2 rounded-md border bg-background p-3" data-testid={testId}>
      <h3 className="text-sm font-semibold">{title}</h3>
      <p className="text-xs text-muted-foreground">{body}</p>
      {children}
    </div>
  );
}
