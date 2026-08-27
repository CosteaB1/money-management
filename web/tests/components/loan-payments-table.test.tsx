import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { LoanPaymentsTable } from '@/src/components/loans/detail/loan-payments-table';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import { formatMoney } from '@/src/lib/utils/currency';
import type { LoanDetailDto } from '@/src/types/api';

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

const LONG_NOTE =
  'Paid back in cash after the birthday dinner, counted twice to be really sure of it.';

function makeDetail(overrides: Partial<LoanDetailDto>): LoanDetailDto {
  return {
    id: 'l0000001-0000-0000-0000-000000000002',
    direction: 'Given',
    counterparty: 'Ion',
    principal: 2500,
    currency: 'MDL',
    loanDate: '2026-03-05',
    totalRepaid: 800,
    outstanding: 1700,
    outstandingMdl: 1700,
    missingFxRate: false,
    status: 'Active',
    paymentCount: 2,
    notes: null,
    isArchived: false,
    createdOn: '2026-03-05',
    disbursementTransactionId: null,
    disbursementAccountId: null,
    disbursementAccountName: null,
    payments: [
      {
        id: 'p-linked',
        amount: 500,
        currency: 'MDL',
        occurredOn: '2026-04-20',
        transactionId: 'tx-loan-payment',
        accountId: '11111111-1111-1111-1111-111111111111',
        accountName: 'Cash Wallet',
        notes: null,
      },
      {
        id: 'p-untracked',
        amount: 300,
        currency: 'MDL',
        occurredOn: '2026-03-15',
        transactionId: null,
        accountId: null,
        accountName: null,
        notes: LONG_NOTE,
      },
    ],
    ...overrides,
  };
}

describe('LoanPaymentsTable', () => {
  it('renders payment rows in the given (newest-first) order with account and notes', () => {
    renderWithClient(<LoanPaymentsTable loan={makeDetail({})} />);

    const rows = screen.getAllByTestId('loan-payment-row');
    expect(rows.length).toBe(2);

    // First row: linked payment with an account name and no notes (em dash).
    const linkedRow = rows[0] as HTMLElement;
    expect(within(linkedRow).getByTestId('loan-payment-amount').textContent).toBe(
      formatMoney(500, 'MDL'),
    );
    expect(within(linkedRow).getByTestId('loan-payment-account')).toHaveTextContent('Cash Wallet');

    // Second row: untracked payment — em-dash account, truncated notes with
    // the full text on the title attribute.
    const untrackedRow = rows[1] as HTMLElement;
    expect(within(untrackedRow).getByTestId('loan-payment-account').textContent).toBe('—');
    const notes = within(untrackedRow).getByTestId('loan-payment-notes');
    expect(notes.textContent?.endsWith('…')).toBe(true);
    expect(notes).toHaveAttribute('title', LONG_NOTE);
  });

  it('renders the empty state when no payments exist', () => {
    renderWithClient(<LoanPaymentsTable loan={makeDetail({ payments: [], paymentCount: 0 })} />);
    expect(screen.getByTestId('loan-payments-empty')).toHaveTextContent(
      'No payments recorded yet.',
    );
  });

  it('warns about the linked transaction in the delete confirm for tracked payments', async () => {
    const user = userEvent.setup();
    renderWithClient(<LoanPaymentsTable loan={makeDetail({})} />);

    // First row is the linked payment.
    await user.click(screen.getAllByTestId('delete-payment')[0] as HTMLElement);

    const dialog = await screen.findByTestId('delete-payment-dialog');
    const warning = within(dialog).getByTestId('delete-payment-linked-warning');
    expect(warning).toHaveTextContent('This will also delete the linked transaction on');
    expect(warning).toHaveTextContent('Cash Wallet');
  });

  it('omits the linked-transaction warning for untracked payments', async () => {
    const user = userEvent.setup();
    renderWithClient(<LoanPaymentsTable loan={makeDetail({})} />);

    // Second row is the untracked payment.
    await user.click(screen.getAllByTestId('delete-payment')[1] as HTMLElement);

    const dialog = await screen.findByTestId('delete-payment-dialog');
    expect(within(dialog).queryByTestId('delete-payment-linked-warning')).not.toBeInTheDocument();
  });

  it('deletes on confirm, hitting the per-payment endpoint, and toasts', async () => {
    const user = userEvent.setup();

    let capturedUrl = '';
    server.use(
      http.delete('*/loans/:id/payments/:paymentId', ({ request }) => {
        capturedUrl = request.url;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const loan = makeDetail({});
    renderWithClient(<LoanPaymentsTable loan={loan} />);

    await user.click(screen.getAllByTestId('delete-payment')[0] as HTMLElement);
    await user.click(await screen.findByTestId('delete-payment-confirm'));

    await waitFor(() => {
      expect(capturedUrl).toContain(`/loans/${loan.id}/payments/p-linked`);
    });
    expect(await screen.findByText('Payment deleted')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByTestId('delete-payment-dialog')).not.toBeInTheDocument();
    });
  });

  it('cancel closes the confirm without deleting', async () => {
    const user = userEvent.setup();

    let deleteCalled = false;
    server.use(
      http.delete('*/loans/:id/payments/:paymentId', () => {
        deleteCalled = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderWithClient(<LoanPaymentsTable loan={makeDetail({})} />);

    await user.click(screen.getAllByTestId('delete-payment')[0] as HTMLElement);
    await screen.findByTestId('delete-payment-dialog');
    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(screen.queryByTestId('delete-payment-dialog')).not.toBeInTheDocument();
    });
    expect(deleteCalled).toBe(false);
  });

  it('shows an error toast when the delete fails', async () => {
    server.use(
      http.delete('*/loans/:id/payments/:paymentId', () =>
        HttpResponse.json({ detail: 'cannot delete' }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<LoanPaymentsTable loan={makeDetail({})} />);

    await user.click(screen.getAllByTestId('delete-payment')[0] as HTMLElement);
    await user.click(await screen.findByTestId('delete-payment-confirm'));

    expect(await screen.findByText('cannot delete')).toBeInTheDocument();
  });
});
