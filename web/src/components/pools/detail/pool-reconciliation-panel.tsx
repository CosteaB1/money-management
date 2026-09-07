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
import type { PoolDetailDto, PoolReconciliationDto, TransactionDirection } from '@/src/types/api';

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
 * Five classes, and each one means something specific:
 *
 * 1. **The account doesn't hold what the ledger says it should** — the whole
 *    ledger replayed as arithmetic against the account's real balance. Rendered
 *    FIRST and deliberately so: it is the only finding that quantifies the
 *    damage, and the four below are the evidence for it.
 * 2. **Money with no entry behind it** — cash moved on the account without a
 *    matching ledger entry. It is being shared pro-rata with the other
 *    participants right now, whoever it belonged to.
 * 3. **Share count disagrees** — the roster's total and the ledger's total have
 *    parted company. Only assertable at all because the owner holds real
 *    shares rather than "whatever is left over".
 * 4. **Priced against a value the ledger cannot reproduce** — a row dated on or
 *    before a priced entry was added, moved or deleted after the fact.
 * 5. **Entries claiming money the account never saw** — the mirror of (2), and
 *    the shape a backfilled arrival that never happened takes.
 *
 * **(1) overlaps (2) and (5) on purpose, and the copy says so.** One stray row
 * typed onto a pooled account trips both (1) and (2); the incident that made
 * (1) exist — 3,000 shares against a 2,000 balance — trips both (1) and (5).
 * Presented as unrelated failures that reads as "two things are broken", which
 * is worse than the truth and sends the user looking for a second cause that
 * isn't there. So (1) carries a cross-reference, and when the cash findings
 * below account for the gap *exactly* it says so outright.
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

      {!r.balanceReconciles && <BalanceFinding pool={pool} />}

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

/**
 * Check 5: the ledger's own arithmetic against the account's real balance.
 *
 * **The two directions are different problems and must not read the same.**
 * Predicted above derived means shares exist for money the account never got —
 * somebody is holding a slice of everyone else's cash. Predicted below means
 * money is sitting there that nothing here explains, and it is quietly being
 * split as though the pool had earned it. The remedy differs too, which is why
 * the copy is written out twice rather than templated over an absolute value.
 *
 * Money is shown as an unsigned amount with a direction-carrying label; a
 * bare "−1,000.00 USD" next to two positive balances is exactly the sort of
 * thing that gets read backwards on the day it matters.
 */
function BalanceFinding({ pool }: Props) {
  const r = pool.reconciliation;
  const ledgerClaimsMore = r.balanceDrift > 0;

  const predicted = formatMoney(r.predictedBalance, pool.currency);
  const derived = formatMoney(r.derivedBalance, pool.currency);
  const gap = formatMoney(Math.abs(r.balanceDrift), pool.currency);

  const opening =
    `Add up everything recorded in this pool — the opening stake, every arrival, ` +
    `every withdrawal, every payout that has settled — and ${pool.accountName} should be ` +
    `holding ${predicted}.`;

  const body = ledgerClaimsMore
    ? `${opening} It actually holds ${derived}, which is ${gap} less. Shares have been issued ` +
      `for it anyway, so whoever holds them is holding a slice of everybody else's money, and ` +
      `everyone who did pay in is being shown less of the pool than they are owed. Nearly ` +
      `always this is an arrival recorded while the pool was being set up whose transaction ` +
      `was never written. Go and look at the real account, not this app: if the ${gap} really ` +
      `is sitting there, the transaction recording it is what's missing — add it, dated the ` +
      `day the money actually arrived. If it isn't there, the money never came, and the shares ` +
      `issued for it are not owed to anyone.`
    : `${opening} It actually holds ${derived}, which is ${gap} more. No shares were issued for ` +
      `it, so it is being split pro-rata across everyone in the pool as though the pool had ` +
      `earned it, whoever the money actually belongs to. Usually it reached the account without ` +
      `being recorded here — a deposit typed straight in, or a gain entered as a transaction ` +
      `rather than as the account's new total — though it can also be an entry here claiming a ` +
      `withdrawal that never actually left. Work out which before recording anything else. ` +
      `Money somebody paid in should be recorded here as money in, and the row typed onto the ` +
      `account deleted: recording money in writes its own transaction, so leaving both would ` +
      `count the same cash twice. And if it is an entry here claiming a withdrawal that never ` +
      `happened, that entry is the thing to fix.`;

  return (
    <Finding
      title={
        ledgerClaimsMore
          ? 'This pool has issued shares for money that never arrived'
          : "The account holds money this pool can't account for"
      }
      body={body}
      testId="reconciliation-balance"
      direction={ledgerClaimsMore ? 'ledger-claims-more' : 'account-holds-more'}
    >
      <dl className="grid gap-2 text-sm sm:grid-cols-3">
        <div>
          <dt className="text-xs text-muted-foreground">
            What this pool&rsquo;s entries add up to
          </dt>
          <dd className="tabular-nums" data-testid="reconciliation-balance-predicted">
            {predicted}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">What the account actually holds</dt>
          <dd className="tabular-nums" data-testid="reconciliation-balance-derived">
            {derived}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">
            {ledgerClaimsMore ? 'Missing from the account' : 'Unaccounted for on the account'}
          </dt>
          <dd className="font-medium tabular-nums" data-testid="reconciliation-balance-drift">
            {gap}
          </dd>
        </div>
      </dl>
      <RelatedCashFindings reconciliation={r} currency={pool.currency} />
    </Finding>
  );
}

/**
 * "One cause, two findings" — the line that stops this panel reading as a
 * longer list of broken things than it is.
 *
 * A stray row on the account moves the derived balance and shows up as an
 * unmatched transaction; an entry claiming cash that never landed moves the
 * predicted balance and shows up as an unbacked claim. Either way the gap above
 * and the rows below are the same mistake seen from two sides. When the signed
 * totals tie out to the cent the note says so plainly, because "fix that and
 * this goes with it" is only safe to promise when the arithmetic actually
 * closes; otherwise it points at them and stops short of the promise.
 */
function RelatedCashFindings({
  reconciliation: r,
  currency,
}: {
  reconciliation: PoolReconciliationDto;
  currency: string;
}) {
  const claims = r.unbackedCashClaims.length;
  const strays = r.unmatchedTransactions.length;
  if (claims + strays === 0) return null;

  const subject =
    claims > 0 && strays > 0
      ? 'the other findings listed below'
      : claims > 0
        ? 'the entries listed below that claim money the account never saw'
        : 'the rows listed below that have no ledger entry behind them';

  const explained = explainedByCashFindings(r, currency);
  const exact = explained !== null && Math.abs(explained - r.balanceDrift) < 0.005;

  return (
    <p className="text-xs text-muted-foreground" data-testid="reconciliation-balance-related">
      {exact
        ? `One problem, not two: this gap is accounted for exactly by ${subject}. Sort those out and this goes with them.`
        : `Read this together with ${subject}: they move this same figure, so it is more likely one mistake showing up twice than two unrelated ones.`}
    </p>
  );
}

/**
 * How much of `balanceDrift` the two cash-shape findings already explain.
 *
 * An unbacked claim inflates the PREDICTED side by its signed cash; an
 * unmatched row inflates the DERIVED side by its own. So the drift they jointly
 * account for is `claims − rows`. Returns null when an unmatched row is in some
 * other currency than the pool's — the account's currency is pinned to the
 * pool's at creation, so that should be unreachable, but summing across
 * currencies to make a claim about someone's money is not a thing to do on a
 * "should".
 */
function explainedByCashFindings(r: PoolReconciliationDto, currency: string): number | null {
  if (r.unmatchedTransactions.some((t) => t.currency !== currency)) return null;

  const signed = (direction: TransactionDirection, amount: number) =>
    direction === 'Income' ? amount : -amount;

  const claimed = r.unbackedCashClaims.reduce((sum, c) => sum + signed(c.direction, c.amount), 0);
  const rows = r.unmatchedTransactions.reduce((sum, t) => sum + signed(t.direction, t.amount), 0);

  return claimed - rows;
}

function Finding({
  title,
  body,
  testId,
  direction,
  children,
}: {
  title: string;
  body: string;
  testId: string;
  /** Optional `data-direction`, so a finding with two readings can be told apart. */
  direction?: string;
  children: React.ReactNode;
}) {
  return (
    <div
      className="space-y-2 rounded-md border bg-background p-3"
      data-testid={testId}
      data-direction={direction}
    >
      <h3 className="text-sm font-semibold">{title}</h3>
      <p className="text-xs text-muted-foreground">{body}</p>
      {children}
    </div>
  );
}
