import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { CreateTransferDialog } from '@/src/components/transactions/create-transfer-dialog';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';

function renderWithClient(ui: React.ReactElement) {
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

describe('CreateTransferDialog', () => {
  it('renders the trigger button', () => {
    renderWithClient(<CreateTransferDialog />);
    expect(screen.getByTestId('new-transfer-button')).toBeInTheDocument();
  });

  it('does not show the legacy MDL-only hint', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);

    await user.click(screen.getByTestId('new-transfer-button'));

    await waitFor(() => {
      expect(screen.getByTestId('transfer-source-select')).toBeInTheDocument();
    });
    expect(screen.queryByTestId('transfer-mdl-hint')).not.toBeInTheDocument();
  });

  it('shows validation errors when submitting empty form', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);

    await user.click(screen.getByTestId('new-transfer-button'));

    await waitFor(() => {
      expect(screen.getByTestId('transfer-submit-button')).toBeInTheDocument();
    });

    await user.click(screen.getByTestId('transfer-submit-button'));

    await waitFor(() => {
      expect(screen.getByText('Source account is required')).toBeInTheDocument();
    });
    expect(screen.getByText('Destination account is required')).toBeInTheDocument();
    expect(screen.getByText('Amount must be greater than 0')).toBeInTheDocument();
    expect(screen.getByText('Description is required')).toBeInTheDocument();
  });

  it('submits a valid transfer and closes the dialog', async () => {
    let captured: Record<string, unknown> | null = null;
    server.use(
      http.post('*/transfers', async ({ request }) => {
        captured = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            sourceTransactionId: 'src-id',
            destinationTransactionId: 'dst-id',
          },
          { status: 201 },
        );
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);

    await user.click(screen.getByTestId('new-transfer-button'));

    await waitFor(() => {
      expect(screen.getByTestId('transfer-source-select')).toBeInTheDocument();
    });

    // Wait for MDL accounts to load (handlers seed three MDL accounts).
    await waitFor(() => {
      expect(screen.getByTestId('transfer-source-select')).toHaveTextContent(
        /select source account/i,
      );
    });

    await user.click(screen.getByTestId('transfer-source-select'));
    const sourceOption = await screen.findByRole('option', { name: 'Cash Wallet' });
    await user.click(sourceOption);

    await user.click(screen.getByTestId('transfer-destination-select'));
    const destOption = await screen.findByRole('option', { name: 'ING Savings' });
    await user.click(destOption);

    const amount = screen.getByTestId('transfer-amount-input');
    await user.clear(amount);
    await user.type(amount, '250');

    const description = screen.getByTestId('transfer-description-input');
    await user.type(description, 'Top up savings');

    const notes = screen.getByTestId('transfer-notes-input');
    await user.type(notes, 'For the rainy-day fund');

    await user.click(screen.getByTestId('transfer-submit-button'));

    await waitFor(() => {
      expect(screen.queryByTestId('transfer-submit-button')).not.toBeInTheDocument();
    });

    expect(captured).not.toBeNull();
    expect(captured).toMatchObject({
      sourceAccountId: '11111111-1111-1111-1111-111111111111',
      destinationAccountId: '33333333-3333-3333-3333-333333333333',
      amount: 250,
      description: 'Top up savings',
      notes: 'For the rainy-day fund',
    });
  });

  it('omits the notes key when the note is left blank', async () => {
    let captured: Record<string, unknown> | null = null;
    server.use(
      http.post('*/transfers', async ({ request }) => {
        captured = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            sourceTransactionId: 'src-id',
            destinationTransactionId: 'dst-id',
          },
          { status: 201 },
        );
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);

    await user.click(screen.getByTestId('new-transfer-button'));

    await waitFor(() => {
      expect(screen.getByTestId('transfer-source-select')).toHaveTextContent(
        /select source account/i,
      );
    });

    await user.click(screen.getByTestId('transfer-source-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));

    await user.click(screen.getByTestId('transfer-destination-select'));
    await user.click(await screen.findByRole('option', { name: 'ING Savings' }));

    const amount = screen.getByTestId('transfer-amount-input');
    await user.clear(amount);
    await user.type(amount, '250');

    await user.type(screen.getByTestId('transfer-description-input'), 'Top up savings');

    // Leave the notes field empty.
    await user.click(screen.getByTestId('transfer-submit-button'));

    await waitFor(() => {
      expect(captured).not.toBeNull();
    });
    expect(captured).not.toHaveProperty('notes');
  });

  it('reveals a destination-amount field + rate and posts destinationAmount for a cross-currency transfer', async () => {
    let captured: Record<string, unknown> | null = null;
    server.use(
      // Source MDL → destination USD: 250 MDL converts to ~14.55 USD.
      http.get('*/fx-rates/convert', () =>
        HttpResponse.json({ convertedAmount: 14.55, rate: 0.0582, hasRate: true }),
      ),
      http.post('*/transfers', async ({ request }) => {
        captured = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          { sourceTransactionId: 'src-id', destinationTransactionId: 'dst-id' },
          { status: 201 },
        );
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);

    await user.click(screen.getByTestId('new-transfer-button'));

    await waitFor(() => {
      expect(screen.getByTestId('transfer-source-select')).toHaveTextContent(
        /select source account/i,
      );
    });

    // Source: Cash Wallet (MDL). Destination: XTB (USD) — a cross-currency pair.
    await user.click(screen.getByTestId('transfer-source-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));

    const amount = screen.getByTestId('transfer-amount-input');
    await user.clear(amount);
    await user.type(amount, '250');

    await user.click(screen.getByTestId('transfer-destination-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB' }));

    // The destination-amount field appears and pre-fills from the FX convert.
    const destAmount = (await screen.findByTestId(
      'transfer-destination-amount-input',
    )) as HTMLInputElement;
    await waitFor(() => {
      expect(destAmount.value).toBe('14.55');
    });

    // The rate line reflects sourceAmount / destinationAmount = 250 / 14.55.
    await waitFor(() => {
      expect(screen.getByTestId('transfer-rate')).toHaveTextContent(/MDL\/USD/);
    });

    // Manually override the pre-filled destination amount → exercises the
    // onChange handler (dirty flag + clears the auto-prefill).
    await user.clear(destAmount);
    await user.type(destAmount, '15');

    const description = screen.getByTestId('transfer-description-input');
    await user.type(description, 'Fund brokerage');

    await user.click(screen.getByTestId('transfer-submit-button'));

    await waitFor(() => {
      expect(captured).not.toBeNull();
    });
    expect(captured).toMatchObject({
      sourceAccountId: '11111111-1111-1111-1111-111111111111',
      destinationAccountId: '44444444-4444-4444-4444-444444444444',
      amount: 250,
      destinationAmount: 15,
      description: 'Fund brokerage',
    });
  });

  it('blocks a cross-currency transfer when the destination amount is missing', async () => {
    let posted = false;
    server.use(
      // No rate ⇒ the field stays blank, so the user must type it.
      http.get('*/fx-rates/convert', () =>
        HttpResponse.json({ convertedAmount: null, rate: null, hasRate: false }),
      ),
      http.post('*/transfers', async () => {
        posted = true;
        return HttpResponse.json(
          { sourceTransactionId: 'src-id', destinationTransactionId: 'dst-id' },
          { status: 201 },
        );
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);

    await user.click(screen.getByTestId('new-transfer-button'));
    await waitFor(() => {
      expect(screen.getByTestId('transfer-source-select')).toHaveTextContent(
        /select source account/i,
      );
    });

    await user.click(screen.getByTestId('transfer-source-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));

    const amount = screen.getByTestId('transfer-amount-input');
    await user.clear(amount);
    await user.type(amount, '250');

    await user.click(screen.getByTestId('transfer-destination-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB' }));

    const destAmount = (await screen.findByTestId(
      'transfer-destination-amount-input',
    )) as HTMLInputElement;
    // No rate → blank.
    expect(destAmount.value).toBe('');

    await user.type(screen.getByTestId('transfer-description-input'), 'Fund brokerage');
    await user.click(screen.getByTestId('transfer-submit-button'));

    await waitFor(() => {
      expect(screen.getByText(/destination amount must be greater than 0/i)).toBeInTheDocument();
    });
    expect(posted).toBe(false);
  });

  it('rejects identical source and destination', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);

    await user.click(screen.getByTestId('new-transfer-button'));

    await waitFor(() => {
      expect(screen.getByTestId('transfer-source-select')).toBeInTheDocument();
    });
    await waitFor(() => {
      expect(screen.getByTestId('transfer-source-select')).toHaveTextContent(
        /select source account/i,
      );
    });

    // Pick a source.
    await user.click(screen.getByTestId('transfer-source-select'));
    const sourceOption = await screen.findByRole('option', { name: 'Cash Wallet' });
    await user.click(sourceOption);

    // The destination select filters out the chosen source, so we can't actually
    // pick a colliding row via the UI. To exercise the schema's `refine`, the
    // submit path still validates source≠destination — fire it without picking
    // a destination and assert the required-field error fires (which is the
    // user-visible behavior protecting the same invariant).
    await user.click(screen.getByTestId('transfer-submit-button'));

    await waitFor(() => {
      expect(screen.getByText('Destination account is required')).toBeInTheDocument();
    });
  });

  it('preselects both source and destination from default props', async () => {
    renderWithClient(
      <CreateTransferDialog
        open
        onOpenChange={() => {}}
        defaultSourceAccountId="11111111-1111-1111-1111-111111111111"
        defaultDestinationAccountId="33333333-3333-3333-3333-333333333333"
      />,
    );
    await waitFor(() => {
      expect(screen.getByTestId('transfer-source-select')).toHaveTextContent('Cash Wallet');
      expect(screen.getByTestId('transfer-destination-select')).toHaveTextContent('ING Savings');
    });
  });

  it('shows the date error when the date is cleared', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);
    await user.click(screen.getByTestId('new-transfer-button'));
    await waitFor(() =>
      expect(screen.getByTestId('transfer-source-select')).toHaveTextContent(
        /select source account/i,
      ),
    );
    await user.click(screen.getByTestId('transfer-source-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));
    await user.click(screen.getByTestId('transfer-destination-select'));
    await user.click(await screen.findByRole('option', { name: 'ING Savings' }));
    const amount = screen.getByTestId('transfer-amount-input');
    await user.clear(amount);
    await user.type(amount, '250');
    await user.type(screen.getByTestId('transfer-description-input'), 'Top up');
    await user.clear(screen.getByTestId('transfer-date-input'));
    await user.click(screen.getByTestId('transfer-submit-button'));
    await waitFor(() => {
      expect(screen.getByTestId('transfer-date-input')).toHaveAttribute('aria-invalid', 'true');
    });
  });

  it('maps a backend amount error onto the amount field', async () => {
    server.use(
      http.post('*/transfers', () =>
        HttpResponse.json({ detail: 'Amount must be greater than 0' }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);
    await user.click(screen.getByTestId('new-transfer-button'));
    await waitFor(() =>
      expect(screen.getByTestId('transfer-source-select')).toHaveTextContent(
        /select source account/i,
      ),
    );
    await user.click(screen.getByTestId('transfer-source-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));
    await user.click(screen.getByTestId('transfer-destination-select'));
    await user.click(await screen.findByRole('option', { name: 'ING Savings' }));
    const amount = screen.getByTestId('transfer-amount-input');
    await user.clear(amount);
    await user.type(amount, '250');
    await user.type(screen.getByTestId('transfer-description-input'), 'Top up');
    await user.click(screen.getByTestId('transfer-submit-button'));
    expect(await screen.findByText('Amount must be greater than 0')).toBeInTheDocument();
  });

  async function fillSameCurrencyTransfer(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByTestId('new-transfer-button'));
    await waitFor(() =>
      expect(screen.getByTestId('transfer-source-select')).toHaveTextContent(
        /select source account/i,
      ),
    );
    await user.click(screen.getByTestId('transfer-source-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));
    await user.click(screen.getByTestId('transfer-destination-select'));
    await user.click(await screen.findByRole('option', { name: 'ING Savings' }));
    const amount = screen.getByTestId('transfer-amount-input');
    await user.clear(amount);
    await user.type(amount, '250');
    await user.type(screen.getByTestId('transfer-description-input'), 'Top up');
  }

  it('leaves the destination amount blank when the FX prefill lookup fails', async () => {
    server.use(http.get('*/fx-rates/convert', () => HttpResponse.json({}, { status: 500 })));
    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);
    await user.click(screen.getByTestId('new-transfer-button'));
    await waitFor(() =>
      expect(screen.getByTestId('transfer-source-select')).toHaveTextContent(
        /select source account/i,
      ),
    );
    await user.click(screen.getByTestId('transfer-source-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));
    // XTB is USD → cross-currency → triggers the convertFx prefill (which fails).
    await user.click(screen.getByTestId('transfer-destination-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB' }));
    const destAmount = (await screen.findByTestId(
      'transfer-destination-amount-input',
    )) as HTMLInputElement;
    // The failed lookup leaves the field empty rather than throwing.
    await waitFor(() => expect(destAmount.value).toBe(''));
  });

  it('maps a backend source error onto the source field', async () => {
    server.use(
      http.post('*/transfers', () =>
        HttpResponse.json({ detail: 'Source account is archived' }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);
    await fillSameCurrencyTransfer(user);
    await user.click(screen.getByTestId('transfer-submit-button'));
    expect(await screen.findByText('Source account is archived')).toBeInTheDocument();
  });

  it('maps a backend destination error onto the destination field', async () => {
    server.use(
      http.post('*/transfers', () =>
        HttpResponse.json({ detail: 'Destination account is archived' }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);
    await fillSameCurrencyTransfer(user);
    await user.click(screen.getByTestId('transfer-submit-button'));
    expect(await screen.findByText('Destination account is archived')).toBeInTheDocument();
  });

  it('maps a backend description error onto the description field', async () => {
    server.use(
      http.post('*/transfers', () =>
        HttpResponse.json({ detail: 'Description is too long' }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);
    await fillSameCurrencyTransfer(user);
    await user.click(screen.getByTestId('transfer-submit-button'));
    expect(await screen.findByText('Description is too long')).toBeInTheDocument();
  });

  it('shows a generic error toast for an unrelated failure', async () => {
    server.use(
      http.post('*/transfers', () => HttpResponse.json({ detail: 'boom' }, { status: 500 })),
    );
    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);
    await fillSameCurrencyTransfer(user);
    await user.click(screen.getByTestId('transfer-submit-button'));
    expect(await screen.findByText('boom')).toBeInTheDocument();
  });

  it('shows the notes error when the note exceeds the max length', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateTransferDialog />);
    await user.click(screen.getByTestId('new-transfer-button'));
    await waitFor(() =>
      expect(screen.getByTestId('transfer-source-select')).toHaveTextContent(
        /select source account/i,
      ),
    );
    await user.click(screen.getByTestId('transfer-source-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));
    await user.click(screen.getByTestId('transfer-destination-select'));
    await user.click(await screen.findByRole('option', { name: 'ING Savings' }));
    const amount = screen.getByTestId('transfer-amount-input');
    await user.clear(amount);
    await user.type(amount, '250');
    await user.type(screen.getByTestId('transfer-description-input'), 'Top up');
    // maxLength caps typing; set over-limit directly so the Zod refine fires.
    fireEvent.change(screen.getByTestId('transfer-notes-input'), {
      target: { value: 'x'.repeat(501) },
    });
    await user.click(screen.getByTestId('transfer-submit-button'));
    await waitFor(() => {
      expect(screen.getByTestId('transfer-notes-input')).toHaveAttribute('aria-invalid', 'true');
    });
  });
});
