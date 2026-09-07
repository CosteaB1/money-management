import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { CreatePoolDialog } from '@/src/components/pools/create-pool-dialog';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import { todayIsoUtc } from '@/src/lib/utils/date';

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

async function openDialog() {
  const user = userEvent.setup();
  renderWithClient(<CreatePoolDialog />);
  await user.click(screen.getByTestId('add-pool-button'));
  await screen.findByTestId('create-pool-dialog');
  return user;
}

describe('CreatePoolDialog', () => {
  it('offers only accounts a pool can actually live in', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));

    // Investment-type, non-archived, not already pooled.
    expect(await screen.findByRole('option', { name: 'ING Savings (MDL)' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'XTB (USD)' })).toBeInTheDocument();
    // Cash and credit cards can never take a balance adjustment, so their NAV
    // could never be re-priced.
    expect(screen.queryByRole('option', { name: /Cash Wallet/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: /BRD Visa/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: /Old Revolut/ })).not.toBeInTheDocument();
  });

  it('hides an account that already has a pool on it', async () => {
    server.use(
      http.get('*/accounts', () =>
        HttpResponse.json([
          {
            id: '44444444-4444-4444-4444-444444444444',
            name: 'XTB',
            type: 'Brokerage',
            currency: 'USD',
            openingDate: '2024-09-10',
            isArchived: false,
            isPooled: true,
            notes: null,
            balance: 1500,
            balanceMdl: 26250,
          },
        ]),
      ),
    );
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    expect(await screen.findByText(/No eligible accounts/)).toBeInTheDocument();
  });

  it('requires an account, a name and the exchange total on the start date', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-submit-button'));

    expect(await screen.findByTestId('pool-account-error')).toHaveTextContent(
      'Pick the account the pool lives in',
    );
    expect(screen.getByTestId('pool-name-error')).toHaveTextContent('Pool name is required');
    expect(screen.getByTestId('pool-inception-value-error')).toHaveTextContent(
      'Type the exchange total on the start date',
    );
  });

  it('derives the currency from the account and never asks for it', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));

    expect(screen.getByTestId('create-pool-dialog')).toHaveTextContent(
      /The pool is denominated in the account.s own currency \(USD\)/,
    );
  });

  it('submits the account, the derived currency and the opening valuation', async () => {
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            id: 'created',
            ownerParticipantId: 'owner',
            seedUnits: 1500,
            markDelta: 0,
            markTransactionId: null,
            participants: [],
          },
          { status: 201 },
        );
      }),
    );

    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1500' },
    });
    await user.click(screen.getByTestId('pool-submit-button'));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({
      accountId: '44444444-4444-4444-4444-444444444444',
      name: 'Binance pool',
      currency: 'USD',
      ownerName: 'Me',
      poolValueAtInception: 1500,
      inceptionDate: todayIsoUtc(),
      backdatedSubscriptions: null,
    });
  });

  it('demands a pre-money total per backdated arrival, because the balance already contains their cash', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1500' },
    });

    await user.click(screen.getByTestId('pool-backfill-toggle'));
    expect(await screen.findByTestId('pool-backfill-row')).toBeInTheDocument();

    await user.type(screen.getByTestId('backfill-name-input-0'), 'Andrei');
    fireEvent.change(screen.getByTestId('backfill-cash-input-0'), { target: { value: '1000' } });
    await user.click(screen.getByTestId('pool-submit-button'));

    expect(await screen.findByTestId('backfill-pre-error-0')).toHaveTextContent(
      'Type the total from just BEFORE their money landed',
    );
  });

  it('sends each backdated arrival priced at its own day', async () => {
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            id: 'created',
            ownerParticipantId: 'owner',
            seedUnits: 1500,
            markDelta: 0,
            markTransactionId: null,
            participants: [],
          },
          { status: 201 },
        );
      }),
    );

    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1500' },
    });

    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await user.type(await screen.findByTestId('backfill-name-input-0'), 'Andrei');
    fireEvent.change(screen.getByTestId('backfill-date-input-0'), {
      target: { value: '2026-08-05' },
    });
    fireEvent.change(screen.getByTestId('backfill-cash-input-0'), { target: { value: '1000' } });
    fireEvent.change(screen.getByTestId('backfill-pre-input-0'), { target: { value: '1500' } });
    await user.click(screen.getByTestId('backfill-write-tx-0'));
    await user.click(screen.getByTestId('pool-submit-button'));

    await waitFor(() => expect(body).not.toBeNull());
    expect((body as unknown as Record<string, unknown>).backdatedSubscriptions).toEqual([
      {
        participantName: 'Andrei',
        occurredOn: '2026-08-05',
        cash: 1000,
        poolValuePreMoney: 1500,
        writeMovementTransaction: true,
      },
    ]);
  });

  it('drops backfill rows again when the toggle goes off, so nothing stale is sent', async () => {
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            id: 'created',
            ownerParticipantId: 'owner',
            seedUnits: 1500,
            markDelta: 0,
            markTransactionId: null,
            participants: [],
          },
          { status: 201 },
        );
      }),
    );

    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1500' },
    });

    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await user.type(await screen.findByTestId('backfill-name-input-0'), 'Andrei');
    // A half-filled row would otherwise both fail validation and be sent.
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await waitFor(() => expect(screen.queryByTestId('pool-backfill-row')).not.toBeInTheDocument());

    await user.click(screen.getByTestId('pool-submit-button'));
    await waitFor(() => expect(body).not.toBeNull());
    expect((body as unknown as Record<string, unknown>).backdatedSubscriptions).toBeNull();
  });

  it('adds and removes extra arrival rows', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await screen.findByTestId('pool-backfill-row');

    await user.click(screen.getByTestId('backfill-add'));
    await waitFor(() => expect(screen.getAllByTestId('pool-backfill-row').length).toBe(2));

    await user.click(screen.getByTestId('backfill-remove-1'));
    await waitFor(() => expect(screen.getAllByTestId('pool-backfill-row').length).toBe(1));
  });

  it('surfaces a server rejection inline rather than closing', async () => {
    server.use(
      http.post('*/pools', () =>
        HttpResponse.json(
          { detail: 'A savings goal already links that account.' },
          { status: 409 },
        ),
      ),
    );

    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1500' },
    });
    await user.click(screen.getByTestId('pool-submit-button'));

    expect(await screen.findByTestId('create-pool-error')).toHaveTextContent(
      'A savings goal already links that account.',
    );
    expect(screen.getByTestId('create-pool-dialog')).toBeInTheDocument();
  });

  it('falls back to a toast on a network failure', async () => {
    server.use(http.post('*/pools', () => HttpResponse.error()));

    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1500' },
    });
    await user.click(screen.getByTestId('pool-submit-button'));

    expect(await screen.findByText(/failed to fetch|network/i)).toBeInTheDocument();
  });

  it('closes and resets when cancelled', async () => {
    const user = await openDialog();
    await user.type(screen.getByTestId('pool-name-input'), 'Scratch');
    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(screen.queryByTestId('create-pool-dialog')).not.toBeInTheDocument());

    await user.click(screen.getByTestId('add-pool-button'));
    expect(await screen.findByTestId('pool-name-input')).toHaveValue('');
  });
});

describe('CreatePoolDialog — a11y', () => {
  it('labels the opening valuation as the whole exchange total, BNB included', async () => {
    await openDialog();
    expect(screen.getByText(/including BNB/i)).toBeInTheDocument();
    expect(screen.getByTestId('create-pool-dialog')).toHaveTextContent(
      /spot, futures, earn, fiat/i,
    );
  });
});

// Guards the one place the dialog silently trusts the server: the mutation's
// success path resets local state, so re-opening must not resurrect the
// backfill section.
describe('CreatePoolDialog — after a successful create', () => {
  it('reopens clean', async () => {
    const onCreated = vi.fn();
    server.use(
      http.post('*/pools', () => {
        onCreated();
        return HttpResponse.json(
          {
            id: 'created',
            ownerParticipantId: 'owner',
            seedUnits: 1500,
            markDelta: 0,
            markTransactionId: null,
            participants: [],
          },
          { status: 201 },
        );
      }),
    );

    const user = await openDialog();
    await user.click(screen.getByTestId('pool-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB (USD)' }));
    await user.type(screen.getByTestId('pool-name-input'), 'Binance pool');
    fireEvent.change(screen.getByTestId('pool-inception-value-input'), {
      target: { value: '1500' },
    });
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await screen.findByTestId('pool-backfill-row');
    await user.type(screen.getByTestId('backfill-name-input-0'), 'Andrei');
    fireEvent.change(screen.getByTestId('backfill-date-input-0'), {
      target: { value: '2026-08-05' },
    });
    fireEvent.change(screen.getByTestId('backfill-cash-input-0'), { target: { value: '1000' } });
    fireEvent.change(screen.getByTestId('backfill-pre-input-0'), { target: { value: '1500' } });
    await user.click(screen.getByTestId('pool-submit-button'));

    await waitFor(() => expect(onCreated).toHaveBeenCalled());
    await waitFor(() => expect(screen.queryByTestId('create-pool-dialog')).not.toBeInTheDocument());

    await user.click(screen.getByTestId('add-pool-button'));
    await screen.findByTestId('create-pool-dialog');
    expect(screen.queryByTestId('pool-backfill-row')).not.toBeInTheDocument();
  });
});

describe('CreatePoolDialog field guards', () => {
  it('requires a name for yourself in the pool', async () => {
    const user = await openDialog();
    const owner = screen.getByTestId('pool-owner-name-input');
    await user.clear(owner);
    await user.click(screen.getByTestId('pool-submit-button'));
    expect(await screen.findByText('Your name is required')).toBeInTheDocument();
  });

  it('refuses a start date in the future', async () => {
    const user = await openDialog();
    const date = screen.getByTestId('pool-inception-input') as HTMLInputElement;
    date.removeAttribute('max');
    fireEvent.change(date, { target: { value: '2100-01-01' } });
    await user.click(screen.getByTestId('pool-submit-button'));
    expect(await screen.findByText('Start date cannot be in the future')).toBeInTheDocument();
  });

  it('rejects an over-long note', async () => {
    const user = await openDialog();
    const notes = screen.getByTestId('pool-notes-input') as HTMLTextAreaElement;
    notes.removeAttribute('maxlength');
    fireEvent.change(notes, { target: { value: 'x'.repeat(501) } });
    await user.click(screen.getByTestId('pool-submit-button'));
    expect(await screen.findByText('Notes must be 500 characters or less')).toBeInTheDocument();
  });

  it('validates every field of a backdated arrival', async () => {
    const user = await openDialog();
    await user.click(screen.getByTestId('pool-backfill-toggle'));
    await screen.findByTestId('pool-backfill-row');

    const date = screen.getByTestId('backfill-date-input-0') as HTMLInputElement;
    date.removeAttribute('max');
    fireEvent.change(date, { target: { value: '2100-01-01' } });
    await user.click(screen.getByTestId('pool-submit-button'));

    expect(await screen.findByText('Name is required')).toBeInTheDocument();
    expect(screen.getByText('Arrival date cannot be in the future')).toBeInTheDocument();
    expect(screen.getByText('Amount must be greater than 0')).toBeInTheDocument();
  });
});
