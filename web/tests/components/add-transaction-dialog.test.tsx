import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { AddTransactionDialog } from '@/src/components/transactions/add-transaction-dialog';
import { server } from '@/src/lib/mocks/server';

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('AddTransactionDialog', () => {
  it('renders the trigger button', () => {
    renderWithClient(<AddTransactionDialog />);
    expect(screen.getByTestId('add-transaction-button')).toBeInTheDocument();
  });

  it('shows validation errors when submitting empty form', async () => {
    const user = userEvent.setup();
    renderWithClient(<AddTransactionDialog />);

    await user.click(screen.getByTestId('add-transaction-button'));

    await waitFor(() => {
      expect(screen.getByTestId('transaction-submit-button')).toBeInTheDocument();
    });

    await user.click(screen.getByTestId('transaction-submit-button'));

    await waitFor(() => {
      expect(screen.getByText('Account is required')).toBeInTheDocument();
    });
    expect(screen.getByText('Amount must be greater than 0')).toBeInTheDocument();
    expect(screen.getByText('Description is required')).toBeInTheDocument();
  });

  it('submits a valid transaction and closes the dialog', async () => {
    const user = userEvent.setup();
    renderWithClient(<AddTransactionDialog />);

    await user.click(screen.getByTestId('add-transaction-button'));

    await waitFor(() => {
      expect(screen.getByTestId('transaction-account-select')).toBeInTheDocument();
    });

    // Wait for accounts to load before opening the select.
    await waitFor(() => {
      // The Radix Select trigger has the placeholder text until a value is chosen.
      expect(screen.getByTestId('transaction-account-select')).toHaveTextContent(/select account/i);
    });

    await user.click(screen.getByTestId('transaction-account-select'));
    const cashOption = await screen.findByRole('option', { name: 'Cash Wallet' });
    await user.click(cashOption);

    const amount = screen.getByTestId('transaction-amount-input');
    await user.clear(amount);
    await user.type(amount, '150.50');

    const description = screen.getByTestId('transaction-description-input');
    await user.type(description, 'Coffee at Tucano');

    await user.click(screen.getByTestId('transaction-submit-button'));

    await waitFor(() => {
      expect(screen.queryByTestId('transaction-submit-button')).not.toBeInTheDocument();
    });
  });

  it('includes notes in the create payload when the Notes field is filled', async () => {
    let postBody: Record<string, unknown> | null = null;
    server.use(
      http.post('*/transactions', async ({ request }) => {
        postBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'created-tx-id' }, { status: 201 });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<AddTransactionDialog />);

    await user.click(screen.getByTestId('add-transaction-button'));

    await waitFor(() => {
      expect(screen.getByTestId('transaction-account-select')).toHaveTextContent(/select account/i);
    });

    await user.click(screen.getByTestId('transaction-account-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));

    const amount = screen.getByTestId('transaction-amount-input');
    await user.clear(amount);
    await user.type(amount, '42');

    await user.type(screen.getByTestId('transaction-description-input'), 'Lunch');
    await user.type(screen.getByTestId('transaction-notes-input'), 'Split with Andrei');

    await user.click(screen.getByTestId('transaction-submit-button'));

    await waitFor(() => {
      expect(postBody).not.toBeNull();
    });
    expect(postBody).toMatchObject({ description: 'Lunch', notes: 'Split with Andrei' });
  });

  it('omits notes from the payload when the Notes field is left blank', async () => {
    let postBody: Record<string, unknown> | null = null;
    server.use(
      http.post('*/transactions', async ({ request }) => {
        postBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'created-tx-id' }, { status: 201 });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<AddTransactionDialog />);

    await user.click(screen.getByTestId('add-transaction-button'));

    await waitFor(() => {
      expect(screen.getByTestId('transaction-account-select')).toHaveTextContent(/select account/i);
    });

    await user.click(screen.getByTestId('transaction-account-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));

    const amount = screen.getByTestId('transaction-amount-input');
    await user.clear(amount);
    await user.type(amount, '42');

    await user.type(screen.getByTestId('transaction-description-input'), 'Lunch');

    await user.click(screen.getByTestId('transaction-submit-button'));

    await waitFor(() => {
      expect(postBody).not.toBeNull();
    });
    expect(postBody).not.toHaveProperty('notes');
  });

  it('shows the transaction-date error when the date is cleared', async () => {
    const user = userEvent.setup();
    renderWithClient(<AddTransactionDialog />);
    await user.click(screen.getByTestId('add-transaction-button'));
    await waitFor(() =>
      expect(screen.getByTestId('transaction-account-select')).toHaveTextContent(/select account/i),
    );
    await user.click(screen.getByTestId('transaction-account-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));
    const amount = screen.getByTestId('transaction-amount-input');
    await user.clear(amount);
    await user.type(amount, '42');
    await user.type(screen.getByTestId('transaction-description-input'), 'Lunch');
    await user.clear(screen.getByTestId('transaction-date-input'));
    await user.click(screen.getByTestId('transaction-submit-button'));
    await waitFor(() => {
      expect(screen.getByTestId('transaction-date-input')).toHaveAttribute('aria-invalid', 'true');
    });
  });

  it('marks the transaction as a transfer with a counter account and switches direction', async () => {
    let postBody: Record<string, unknown> | null = null;
    server.use(
      http.post('*/transactions', async ({ request }) => {
        postBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'created-tx-id' }, { status: 201 });
      }),
    );
    const user = userEvent.setup();
    renderWithClient(<AddTransactionDialog />);
    await user.click(screen.getByTestId('add-transaction-button'));
    await waitFor(() =>
      expect(screen.getByTestId('transaction-account-select')).toHaveTextContent(/select account/i),
    );
    await user.click(screen.getByTestId('transaction-account-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));

    // Switch direction to Income (exercises the radio onChange + category reset).
    await user.click(screen.getByRole('radio', { name: 'Income' }));

    const amt = screen.getByTestId('transaction-amount-input');
    await user.clear(amt);
    await user.type(amt, '100');
    await user.type(screen.getByTestId('transaction-description-input'), 'Transfer in');

    // Toggle the internal-transfer switch, then pick a counter account.
    await user.click(screen.getByTestId('transaction-is-transfer'));
    await user.click(await screen.findByTestId('transaction-counter-account-select'));
    await user.click(await screen.findByRole('option', { name: 'ING Savings' }));

    await user.click(screen.getByTestId('transaction-submit-button'));
    await waitFor(() => expect(postBody).not.toBeNull());
    expect(postBody).toMatchObject({
      direction: 'Income',
      isTransfer: true,
      counterAccountId: '33333333-3333-3333-3333-333333333333',
    });
  });

  it('maps a backend amount error onto the amount field', async () => {
    server.use(
      http.post('*/transactions', () =>
        HttpResponse.json({ detail: 'Amount must be positive' }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<AddTransactionDialog />);
    await user.click(screen.getByTestId('add-transaction-button'));
    await waitFor(() =>
      expect(screen.getByTestId('transaction-account-select')).toHaveTextContent(/select account/i),
    );
    await user.click(screen.getByTestId('transaction-account-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));
    const amt = screen.getByTestId('transaction-amount-input');
    await user.clear(amt);
    await user.type(amt, '5');
    await user.type(screen.getByTestId('transaction-description-input'), 'X');
    await user.click(screen.getByTestId('transaction-submit-button'));
    expect(await screen.findByText('Amount must be positive')).toBeInTheDocument();
  });

  it('maps a backend description error onto the description field', async () => {
    server.use(
      http.post('*/transactions', () =>
        HttpResponse.json({ detail: 'Description is required' }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<AddTransactionDialog />);
    await user.click(screen.getByTestId('add-transaction-button'));
    await waitFor(() =>
      expect(screen.getByTestId('transaction-account-select')).toHaveTextContent(/select account/i),
    );
    await user.click(screen.getByTestId('transaction-account-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));
    const amt = screen.getByTestId('transaction-amount-input');
    await user.clear(amt);
    await user.type(amt, '5');
    await user.type(screen.getByTestId('transaction-description-input'), 'X');
    await user.click(screen.getByTestId('transaction-submit-button'));
    expect((await screen.findAllByText('Description is required')).length).toBeGreaterThan(0);
  });

  it('maps a backend account error onto the account field', async () => {
    server.use(
      http.post('*/transactions', () =>
        HttpResponse.json({ detail: 'Account not found' }, { status: 404 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<AddTransactionDialog />);
    await user.click(screen.getByTestId('add-transaction-button'));
    await waitFor(() =>
      expect(screen.getByTestId('transaction-account-select')).toHaveTextContent(/select account/i),
    );
    await user.click(screen.getByTestId('transaction-account-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));
    const amt = screen.getByTestId('transaction-amount-input');
    await user.clear(amt);
    await user.type(amt, '5');
    await user.type(screen.getByTestId('transaction-description-input'), 'X');
    await user.click(screen.getByTestId('transaction-submit-button'));
    expect(await screen.findByText('Account not found')).toBeInTheDocument();
  });

  it('shows a generic error toast when the failure is unrelated to a field', async () => {
    server.use(
      http.post('*/transactions', () =>
        HttpResponse.json({ detail: 'Server exploded' }, { status: 500 }),
      ),
    );
    const { Toaster } = await import('@/src/components/ui/sonner');
    const user = userEvent.setup();
    render(
      <QueryClientProvider
        client={
          new QueryClient({
            defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
          })
        }
      >
        <AddTransactionDialog />
        <Toaster />
      </QueryClientProvider>,
    );
    await user.click(screen.getByTestId('add-transaction-button'));
    await waitFor(() =>
      expect(screen.getByTestId('transaction-account-select')).toHaveTextContent(/select account/i),
    );
    await user.click(screen.getByTestId('transaction-account-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));
    const amt = screen.getByTestId('transaction-amount-input');
    await user.clear(amt);
    await user.type(amt, '5');
    await user.type(screen.getByTestId('transaction-description-input'), 'X');
    await user.click(screen.getByTestId('transaction-submit-button'));
    expect(await screen.findByText('Server exploded')).toBeInTheDocument();
  });

  it('shows the notes error when the note exceeds the max length', async () => {
    const user = userEvent.setup();
    renderWithClient(<AddTransactionDialog />);
    await user.click(screen.getByTestId('add-transaction-button'));
    await waitFor(() =>
      expect(screen.getByTestId('transaction-account-select')).toHaveTextContent(/select account/i),
    );
    await user.click(screen.getByTestId('transaction-account-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));
    const amount = screen.getByTestId('transaction-amount-input');
    await user.clear(amount);
    await user.type(amount, '42');
    await user.type(screen.getByTestId('transaction-description-input'), 'Lunch');
    fireEvent.change(screen.getByTestId('transaction-notes-input'), {
      target: { value: 'x'.repeat(501) },
    });
    await user.click(screen.getByTestId('transaction-submit-button'));
    await waitFor(() => {
      expect(screen.getByTestId('transaction-notes-input')).toHaveAttribute('aria-invalid', 'true');
    });
  });
});
