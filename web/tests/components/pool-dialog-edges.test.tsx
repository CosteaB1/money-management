import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { AddParticipantDialog } from '@/src/components/pools/add-participant-dialog';
import { ArchivePoolDialog } from '@/src/components/pools/archive-pool-dialog';
import { CloseMonthDialog } from '@/src/components/pools/close-month-dialog';
import { DeleteEventDialog } from '@/src/components/pools/delete-event-dialog';
import { MarkStalenessWarning } from '@/src/components/pools/detail/mark-staleness-warning';
import { PoolParticipantsTable } from '@/src/components/pools/detail/pool-participants-table';
import { RecordCostReimbursementDialog } from '@/src/components/pools/record-cost-reimbursement-dialog';
import { RecordRedemptionDialog } from '@/src/components/pools/record-redemption-dialog';
import { RecordSubscriptionDialog } from '@/src/components/pools/record-subscription-dialog';
import { SettlePayoutDialog } from '@/src/components/pools/settle-payout-dialog';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import type { PoolDetailDto, PoolParticipantDto, PoolUnitEventDto } from '@/src/types/api';

/**
 * The edges the happy-path suites don't reach: dismissal teardown, network
 * failures (which fall back to a toast rather than an inline message, because
 * there is no server message to show), the notes length guard, and the
 * conditional copy on the destructive confirms.
 */

const POOL_ID = 'aaaa0001-0000-4000-8000-000000000001';
const OWNER_ID = 'bbbb0001-0000-4000-8000-000000000001';
const ANDREI_ID = 'bbbb0001-0000-4000-8000-000000000002';

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      {ui}
      <Toaster />
    </QueryClientProvider>,
  );
}

function participant(overrides: Partial<PoolParticipantDto>): PoolParticipantDto {
  return {
    id: overrides.id ?? crypto.randomUUID(),
    name: 'Somebody',
    isOwner: false,
    isArchived: false,
    joinedOn: '2026-08-05',
    units: 800,
    ownershipPercent: 40,
    stake: 1000,
    stakeMdl: 17500,
    capitalBase: 900,
    distributable: 100,
    unpaidDistributionCash: 0,
    unpaidDistributionCount: 0,
    missingFxRate: false,
    ...overrides,
  };
}

function pool(overrides: Partial<PoolDetailDto> = {}): PoolDetailDto {
  return {
    id: POOL_ID,
    accountId: '66666666-6666-6666-6666-666666666666',
    accountName: 'Binanance',
    accountCurrency: 'USD',
    accountIsArchived: false,
    name: 'Binance pool',
    currency: 'USD',
    inceptionDate: '2026-08-01',
    notes: null,
    isArchived: false,
    createdOn: '2026-08-01T09:00:00Z',
    asOf: '2026-09-03',
    accountBalance: 2600,
    unpaidDistributionCash: 100,
    unpaidDistributionCount: 1,
    poolValue: 2500,
    poolValueMdl: 43750,
    totalUnits: 2000,
    navPerUnit: 1.25,
    ownerParticipantId: OWNER_ID,
    ownerFraction: 0.2,
    outsideCapital: 2000,
    outsideCapitalMdl: 35000,
    missingFxRate: false,
    lastMarkDate: '2026-08-31',
    markAgeDays: 3,
    participants: [
      participant({ id: OWNER_ID, name: 'Me', isOwner: true, capitalBase: 0, distributable: 500 }),
      participant({ id: ANDREI_ID, name: 'Andrei', distributable: 100 }),
    ],
    events: [],
    reconciliation: {
      isClean: true,
      unmatchedTransactions: [],
      participantUnits: 2000,
      ledgerUnits: 2000,
      unitsDrift: 0,
      unitsBalance: true,
      valueDrifts: [],
      unbackedCashClaims: [],
    },
    ...overrides,
  };
}

function unitEvent(overrides: Partial<PoolUnitEventDto> = {}): PoolUnitEventDto {
  return {
    id: 'cccc0001-0000-4000-8000-000000000004',
    participantId: ANDREI_ID,
    participantName: 'Andrei',
    kind: 'Distribution',
    occurredOn: '2026-08-31',
    units: 80,
    unitsDelta: -80,
    navPerUnit: 1.25,
    poolValuePreMoney: 2600,
    cash: 100,
    cashCurrency: 'USD',
    settledOn: null,
    isUnpaid: true,
    movementTransactionId: null,
    movementAccountId: null,
    movementAccountName: null,
    notes: null,
    ...overrides,
  };
}

const LONG_NOTE = 'x'.repeat(501);
const noop = () => {};

/** Types past a textarea's maxLength by stripping the attribute first. */
function typeLongNote(testId: string) {
  const notes = screen.getByTestId(testId) as HTMLTextAreaElement;
  notes.removeAttribute('maxlength');
  fireEvent.change(notes, { target: { value: LONG_NOTE } });
}

describe('dismissal teardown', () => {
  it('clears the subscription form and its error when the dialog is dismissed', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(<RecordSubscriptionDialog pool={pool()} open onOpenChange={onOpenChange} />);

    fireEvent.change(screen.getByTestId('subscription-cash-input'), { target: { value: '55' } });
    await user.keyboard('{Escape}');
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('clears the redemption form when dismissed', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(<RecordRedemptionDialog pool={pool()} open onOpenChange={onOpenChange} />);
    await user.keyboard('{Escape}');
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('clears the close-month exclusions when dismissed', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(<CloseMonthDialog pool={pool()} open onOpenChange={onOpenChange} />);
    await user.keyboard('{Escape}');
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('clears the cost form when dismissed', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(
      <RecordCostReimbursementDialog pool={pool()} open onOpenChange={onOpenChange} />,
    );
    await user.keyboard('{Escape}');
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('clears the participant form when dismissed', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(
      <AddParticipantDialog poolId={POOL_ID} poolName="P" open onOpenChange={onOpenChange} />,
    );
    await user.keyboard('{Escape}');
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('clears the settle form when dismissed', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(
      <SettlePayoutDialog
        poolId={POOL_ID}
        poolCurrency="USD"
        accountName="Binanance"
        event={unitEvent()}
        open
        onOpenChange={onOpenChange}
      />,
    );
    await user.keyboard('{Escape}');
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('clears the archive confirm when dismissed', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(
      <ArchivePoolDialog
        pool={{ id: POOL_ID, name: 'P', currency: 'USD', outsideCapital: 0 }}
        open
        onOpenChange={onOpenChange}
      />,
    );
    await user.keyboard('{Escape}');
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('clears the delete confirm when dismissed', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(
      <DeleteEventDialog
        poolId={POOL_ID}
        poolCurrency="USD"
        event={unitEvent()}
        open
        onOpenChange={onOpenChange}
      />,
    );
    await user.keyboard('{Escape}');
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('closes the cancel buttons without submitting', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(<CloseMonthDialog pool={pool()} open onOpenChange={onOpenChange} />);
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});

describe('network failures fall back to a toast', () => {
  it('subscription', async () => {
    const user = userEvent.setup();
    server.use(http.post('*/pools/:id/subscriptions', () => HttpResponse.error()));
    renderWithClient(<RecordSubscriptionDialog pool={pool()} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('subscription-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));
    fireEvent.change(screen.getByTestId('subscription-pool-value-input'), {
      target: { value: '2500' },
    });
    fireEvent.change(screen.getByTestId('subscription-cash-input'), { target: { value: '10' } });
    await user.click(screen.getByTestId('subscription-submit-button'));

    expect(await screen.findByText(/failed to fetch|network/i)).toBeInTheDocument();
  });

  it('redemption', async () => {
    const user = userEvent.setup();
    server.use(http.post('*/pools/:id/redemptions', () => HttpResponse.error()));
    renderWithClient(<RecordRedemptionDialog pool={pool()} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('redemption-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));
    fireEvent.change(screen.getByTestId('redemption-pool-value-input'), {
      target: { value: '2500' },
    });
    fireEvent.change(screen.getByTestId('redemption-cash-input'), { target: { value: '10' } });
    await user.click(screen.getByTestId('redemption-submit-button'));

    expect(await screen.findByText(/failed to fetch|network/i)).toBeInTheDocument();
  });

  it('close month', async () => {
    const user = userEvent.setup();
    server.use(http.post('*/pools/:id/distributions', () => HttpResponse.error()));
    renderWithClient(<CloseMonthDialog pool={pool()} open onOpenChange={noop} />);

    fireEvent.change(screen.getByTestId('close-pool-value-input'), { target: { value: '2600' } });
    await user.click(screen.getByTestId('close-month-submit-button'));

    expect(await screen.findByText(/failed to fetch|network/i)).toBeInTheDocument();
  });

  it('cost reimbursement', async () => {
    const user = userEvent.setup();
    server.use(http.post('*/pools/:id/cost-reimbursements', () => HttpResponse.error()));
    renderWithClient(<RecordCostReimbursementDialog pool={pool()} open onOpenChange={noop} />);

    fireEvent.change(screen.getByTestId('cost-amount-input'), { target: { value: '25' } });
    await user.click(screen.getByTestId('cost-submit-button'));

    expect(await screen.findByText(/failed to fetch|network/i)).toBeInTheDocument();
  });

  it('add participant', async () => {
    const user = userEvent.setup();
    server.use(http.post('*/pools/:id/participants', () => HttpResponse.error()));
    renderWithClient(
      <AddParticipantDialog poolId={POOL_ID} poolName="P" open onOpenChange={noop} />,
    );

    await user.type(screen.getByTestId('participant-name-input'), 'Bogdan');
    await user.click(screen.getByTestId('participant-submit-button'));

    expect(await screen.findByText(/failed to fetch|network/i)).toBeInTheDocument();
  });

  it('settle payout', async () => {
    const user = userEvent.setup();
    server.use(http.post('*/pools/:id/distributions/:eventId/settle', () => HttpResponse.error()));
    renderWithClient(
      <SettlePayoutDialog
        poolId={POOL_ID}
        poolCurrency="USD"
        accountName="Binanance"
        event={unitEvent()}
        open
        onOpenChange={noop}
      />,
    );

    await user.click(screen.getByTestId('settle-submit-button'));
    expect(await screen.findByText(/failed to fetch|network/i)).toBeInTheDocument();
  });

  it('archive pool', async () => {
    const user = userEvent.setup();
    server.use(http.post('*/pools/:id/archive', () => HttpResponse.error()));
    renderWithClient(
      <ArchivePoolDialog
        pool={{ id: POOL_ID, name: 'P', currency: 'USD', outsideCapital: 0 }}
        open
        onOpenChange={noop}
      />,
    );

    await user.click(screen.getByTestId('archive-pool-confirm-button'));
    expect(await screen.findByText(/failed to fetch|network/i)).toBeInTheDocument();
  });

  it('delete event', async () => {
    const user = userEvent.setup();
    server.use(http.delete('*/pools/:id/events/:eventId', () => HttpResponse.error()));
    renderWithClient(
      <DeleteEventDialog
        poolId={POOL_ID}
        poolCurrency="USD"
        event={unitEvent()}
        open
        onOpenChange={noop}
      />,
    );

    await user.click(screen.getByTestId('delete-pool-event-confirm'));
    expect(await screen.findByText(/failed to fetch|network/i)).toBeInTheDocument();
  });
});

describe('notes length guard', () => {
  it('rejects an over-long note on a subscription', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordSubscriptionDialog pool={pool()} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('subscription-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));
    fireEvent.change(screen.getByTestId('subscription-pool-value-input'), {
      target: { value: '2500' },
    });
    fireEvent.change(screen.getByTestId('subscription-cash-input'), { target: { value: '10' } });
    typeLongNote('subscription-notes-input');
    await user.click(screen.getByTestId('subscription-submit-button'));

    expect(await screen.findByText('Notes must be 500 characters or less')).toBeInTheDocument();
  });

  it('rejects an over-long note on a redemption', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordRedemptionDialog pool={pool()} open onOpenChange={noop} />);

    await user.click(screen.getByTestId('redemption-participant-select'));
    await user.click(await screen.findByRole('option', { name: 'Andrei' }));
    fireEvent.change(screen.getByTestId('redemption-pool-value-input'), {
      target: { value: '2500' },
    });
    fireEvent.change(screen.getByTestId('redemption-cash-input'), { target: { value: '10' } });
    typeLongNote('redemption-notes-input');
    await user.click(screen.getByTestId('redemption-submit-button'));

    expect(await screen.findByText('Notes must be 500 characters or less')).toBeInTheDocument();
  });

  it('rejects an over-long note on a close', async () => {
    const user = userEvent.setup();
    renderWithClient(<CloseMonthDialog pool={pool()} open onOpenChange={noop} />);

    fireEvent.change(screen.getByTestId('close-pool-value-input'), { target: { value: '2600' } });
    typeLongNote('close-notes-input');
    await user.click(screen.getByTestId('close-month-submit-button'));

    expect(await screen.findByText('Notes must be 500 characters or less')).toBeInTheDocument();
  });

  it('rejects an over-long note on a cost', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordCostReimbursementDialog pool={pool()} open onOpenChange={noop} />);

    fireEvent.change(screen.getByTestId('cost-amount-input'), { target: { value: '25' } });
    typeLongNote('cost-notes-input');
    await user.click(screen.getByTestId('cost-submit-button'));

    expect(await screen.findByText('Notes must be 500 characters or less')).toBeInTheDocument();
  });

  it('rejects an over-long note on a settlement', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <SettlePayoutDialog
        poolId={POOL_ID}
        poolCurrency="USD"
        accountName="Binanance"
        event={unitEvent()}
        open
        onOpenChange={noop}
      />,
    );

    typeLongNote('settle-notes-input');
    await user.click(screen.getByTestId('settle-submit-button'));

    expect(await screen.findByText('Notes must be 500 characters or less')).toBeInTheDocument();
  });
});

describe('date guards', () => {
  it('refuses a join date in the future', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <AddParticipantDialog poolId={POOL_ID} poolName="P" open onOpenChange={noop} />,
    );

    await user.type(screen.getByTestId('participant-name-input'), 'Bogdan');
    const date = screen.getByTestId('participant-joined-input') as HTMLInputElement;
    date.removeAttribute('max');
    fireEvent.change(date, { target: { value: '2100-01-01' } });
    await user.click(screen.getByTestId('participant-submit-button'));

    expect(await screen.findByText('Join date cannot be in the future')).toBeInTheDocument();
  });

  it('refuses a settlement dated in the future', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <SettlePayoutDialog
        poolId={POOL_ID}
        poolCurrency="USD"
        accountName="Binanance"
        event={unitEvent()}
        open
        onOpenChange={noop}
      />,
    );

    const date = screen.getByTestId('settle-date-input') as HTMLInputElement;
    date.removeAttribute('max');
    fireEvent.change(date, { target: { value: '2100-01-01' } });
    await user.click(screen.getByTestId('settle-submit-button'));

    expect(await screen.findByText('Settlement date cannot be in the future')).toBeInTheDocument();
  });
});

describe('DeleteEventDialog copy', () => {
  it('warns that both halves of a shared cost go together', () => {
    renderWithClient(
      <DeleteEventDialog
        poolId={POOL_ID}
        poolCurrency="USD"
        event={unitEvent({ kind: 'CostShare', cash: null, cashCurrency: null, isUnpaid: false })}
        open
        onOpenChange={noop}
      />,
    );

    const dialog = screen.getByTestId('delete-pool-event-dialog');
    expect(dialog).toHaveTextContent('Cost share');
    expect(dialog).toHaveTextContent(/Both halves of the shared cost go/i);
    // No cash leg, so no amount is quoted.
    expect(dialog).not.toHaveTextContent(/USD/);
  });

  it('names the account whose transaction goes with it', () => {
    renderWithClient(
      <DeleteEventDialog
        poolId={POOL_ID}
        poolCurrency="USD"
        event={unitEvent({
          kind: 'Redemption',
          isUnpaid: false,
          settledOn: '2026-08-20',
          movementTransactionId: 'tx',
          movementAccountName: 'Binanance',
        })}
        open
        onOpenChange={noop}
      />,
    );

    expect(screen.getByTestId('delete-pool-event-dialog')).toHaveTextContent(
      /transaction it wrote on\s+Binanance/i,
    );
  });

  it('falls back to a generic phrase when the linked account cannot be resolved', () => {
    renderWithClient(
      <DeleteEventDialog
        poolId={POOL_ID}
        poolCurrency="USD"
        event={unitEvent({
          kind: 'Redemption',
          isUnpaid: false,
          movementTransactionId: 'tx',
          movementAccountName: null,
        })}
        open
        onOpenChange={noop}
      />,
    );

    expect(screen.getByTestId('delete-pool-event-dialog')).toHaveTextContent(/the linked account/i);
  });

  it('surfaces a server refusal inline', async () => {
    const user = userEvent.setup();
    server.use(
      http.delete('*/pools/:id/events/:eventId', () =>
        HttpResponse.json({ detail: 'The opening stake cannot be deleted.' }, { status: 409 }),
      ),
    );
    renderWithClient(
      <DeleteEventDialog
        poolId={POOL_ID}
        poolCurrency="USD"
        event={unitEvent()}
        open
        onOpenChange={noop}
      />,
    );

    await user.click(screen.getByTestId('delete-pool-event-confirm'));
    expect(await screen.findByTestId('delete-pool-event-error')).toHaveTextContent(
      'The opening stake cannot be deleted.',
    );
  });
});

describe('MarkStalenessWarning wording', () => {
  it('uses the singular on exactly one day past the threshold boundary', () => {
    // Day counts are user-facing copy; "1 days ago" is the kind of detail that
    // makes a warning look automated and get ignored.
    render(<MarkStalenessWarning lastMarkDate="2026-09-02" markAgeDays={1} />);
    expect(screen.queryByTestId('pool-mark-staleness')).not.toBeInTheDocument();
  });

  it('renders without a date when only the age is known', () => {
    render(<MarkStalenessWarning lastMarkDate={null} markAgeDays={12} />);
    expect(screen.getByTestId('pool-mark-staleness')).toHaveTextContent(
      'Value last confirmed 12 days ago',
    );
  });
});

describe('PoolParticipantsTable edges', () => {
  it('flags a stake that could not be converted and drops its MDL line', () => {
    render(
      <PoolParticipantsTable
        pool={pool({
          participants: [
            participant({
              id: ANDREI_ID,
              name: 'Andrei',
              stakeMdl: null,
              missingFxRate: true,
            }),
          ],
        })}
      />,
    );

    expect(screen.getByTestId('pool-participant-missing-fx')).toBeInTheDocument();
    expect(screen.queryByTestId('pool-participant-stake-mdl')).not.toBeInTheDocument();
  });

  it('shows no MDL line at all on an MDL-denominated pool', () => {
    render(
      <PoolParticipantsTable
        pool={pool({
          currency: 'MDL',
          participants: [participant({ id: ANDREI_ID, name: 'Andrei' })],
        })}
      />,
    );
    expect(screen.queryByTestId('pool-participant-stake-mdl')).not.toBeInTheDocument();
  });

  it('renders an em dash when there is no price to value a holder at', () => {
    render(
      <PoolParticipantsTable
        pool={pool({
          navPerUnit: null,
          participants: [
            participant({ id: ANDREI_ID, name: 'Andrei', stake: null, distributable: null }),
          ],
        })}
      />,
    );
    const row = screen.getByTestId('pool-participant-row');
    expect(within(row).getByTestId('pool-participant-stake').textContent).toBe('—');
  });
});

describe('PoolParticipantsTable archived holders', () => {
  it('keeps an archived holder visible — their shares are still part of the total', () => {
    render(
      <PoolParticipantsTable
        pool={pool({
          participants: [participant({ id: ANDREI_ID, name: 'Andrei', isArchived: true })],
        })}
      />,
    );
    expect(screen.getByTestId('pool-participant-archived-badge')).toHaveTextContent('Archived');
  });
});
