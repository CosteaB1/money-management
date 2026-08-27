import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { LoanDetailView } from '@/src/components/loans/detail/loan-detail-view';
import { server } from '@/src/lib/mocks/server';
import { formatMoney } from '@/src/lib/utils/currency';
import type { LoanDetailDto } from '@/src/types/api';

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

function makeDetail(overrides: Partial<LoanDetailDto>): LoanDetailDto {
  return {
    id: 'override-loan',
    direction: 'Given',
    counterparty: 'Somebody',
    principal: 1000,
    currency: 'MDL',
    loanDate: '2026-01-01',
    totalRepaid: 0,
    outstanding: 1000,
    outstandingMdl: 1000,
    missingFxRate: false,
    status: 'Active',
    paymentCount: 0,
    notes: null,
    isArchived: false,
    createdOn: '2026-01-01',
    disbursementTransactionId: null,
    disbursementAccountId: null,
    disbursementAccountName: null,
    payments: [],
    ...overrides,
  };
}

describe('LoanDetailView', () => {
  it('renders the skeleton during the initial fetch', async () => {
    server.use(
      http.get('*/loans/:id', async () => {
        // Keep the promise pending long enough for the loading branch to
        // be observable in the test.
        await new Promise((r) => setTimeout(r, 50));
        return HttpResponse.json(makeDetail({}));
      }),
    );

    renderWithClient(<LoanDetailView id="l0000001-0000-0000-0000-000000000001" />);
    expect(screen.getByTestId('loan-detail-skeleton')).toBeInTheDocument();
  });

  it('renders the full detail view for the borrowed EUR seed loan', async () => {
    renderWithClient(<LoanDetailView id="l0000001-0000-0000-0000-000000000001" />);

    await waitFor(() => {
      expect(screen.getByTestId('loan-detail-view')).toBeInTheDocument();
    });
    expect(screen.getByTestId('loan-detail-counterparty')).toHaveTextContent('Parents');
    expect(screen.getByTestId('loan-detail-direction')).toHaveTextContent('Borrowed');
    expect(screen.getByTestId('loan-detail-status')).toHaveTextContent('Active');
    expect(screen.queryByTestId('loan-detail-archived')).not.toBeInTheDocument();

    // Progress card: native outstanding + muted MDL-eq + repaid subtitle.
    expect(screen.getByTestId('loan-detail-outstanding').textContent).toBe(
      formatMoney(3000, 'EUR'),
    );
    expect(screen.getByTestId('loan-detail-outstanding-mdl').textContent).toBe(
      formatMoney(57600, 'MDL'),
    );
    expect(screen.getByTestId('loan-detail-progress-subtitle').textContent).toContain(
      formatMoney(2000, 'EUR'),
    );

    // Untracked disbursement → no "Disbursed via" line.
    expect(screen.queryByTestId('loan-detail-disbursement')).not.toBeInTheDocument();

    // Payment history renders as given (two seed rows).
    expect(screen.getAllByTestId('loan-payment-row').length).toBe(2);

    // Actions are available on a non-archived active loan.
    expect(screen.getByTestId('loan-detail-record-payment')).toBeInTheDocument();
    expect(screen.getByTestId('loan-detail-edit')).toBeInTheDocument();
    expect(screen.getByTestId('loan-detail-archive')).toBeInTheDocument();
  });

  it('renders the disbursement line for an account-linked loan and no MDL-eq for MDL', async () => {
    renderWithClient(<LoanDetailView id="l0000001-0000-0000-0000-000000000002" />);

    await waitFor(() => {
      expect(screen.getByTestId('loan-detail-view')).toBeInTheDocument();
    });
    expect(screen.getByTestId('loan-detail-direction')).toHaveTextContent('Lent');
    const disbursement = screen.getByTestId('loan-detail-disbursement');
    expect(disbursement).toHaveTextContent('Disbursed via');
    expect(disbursement).toHaveTextContent('Cash Wallet');
    // MDL loan → the identity conversion line is omitted.
    expect(screen.queryByTestId('loan-detail-outstanding-mdl')).not.toBeInTheDocument();
  });

  it('hides all mutating actions on an archived loan and shows the Archived badge', async () => {
    server.use(
      http.get('*/loans/:id', () =>
        HttpResponse.json(makeDetail({ isArchived: true, counterparty: 'Old debt' })),
      ),
    );

    renderWithClient(<LoanDetailView id="override-loan" />);

    await waitFor(() => {
      expect(screen.getByTestId('loan-detail-archived')).toBeInTheDocument();
    });
    // Archived swaps the action group for a single Unarchive button.
    expect(screen.getByTestId('loan-detail-unarchive')).toBeInTheDocument();
    expect(screen.queryByTestId('loan-detail-record-payment')).not.toBeInTheDocument();
    expect(screen.queryByTestId('loan-detail-edit')).not.toBeInTheDocument();
    expect(screen.queryByTestId('loan-detail-archive')).not.toBeInTheDocument();
  });

  it('hides only "Record payment" on a settled loan and caps the bar at 100%', async () => {
    server.use(
      http.get('*/loans/:id', () =>
        HttpResponse.json(
          makeDetail({
            status: 'Settled',
            totalRepaid: 1000,
            outstanding: 0,
            outstandingMdl: 0,
          }),
        ),
      ),
    );

    renderWithClient(<LoanDetailView id="override-loan" />);

    await waitFor(() => {
      expect(screen.getByTestId('loan-detail-status')).toHaveTextContent('Settled');
    });
    expect(screen.queryByTestId('loan-detail-record-payment')).not.toBeInTheDocument();
    expect(screen.getByTestId('loan-detail-edit')).toBeInTheDocument();
    const bar = screen.getByTestId('loan-detail-progress-bar');
    expect(bar.getAttribute('style') ?? '').toMatch(/width:\s*100%/);
    expect(bar.getAttribute('data-status')).toBe('Settled');
  });

  it('surfaces the missing-FX warning on the progress card', async () => {
    server.use(
      http.get('*/loans/:id', () =>
        HttpResponse.json(
          makeDetail({ currency: 'GBP', outstandingMdl: null, missingFxRate: true }),
        ),
      ),
    );

    renderWithClient(<LoanDetailView id="override-loan" />);

    await waitFor(() => {
      expect(screen.getByTestId('loan-detail-missing-fx')).toBeInTheDocument();
    });
    // No convertible rate → no MDL-eq line either.
    expect(screen.queryByTestId('loan-detail-outstanding-mdl')).not.toBeInTheDocument();
  });

  it('renders the not-found error state when the backend returns 404', async () => {
    renderWithClient(<LoanDetailView id="does-not-exist" />);

    await waitFor(() => {
      const errorBlock = screen.getByTestId('loan-detail-error');
      expect(errorBlock).toBeInTheDocument();
      expect(errorBlock.getAttribute('data-not-found')).toBe('true');
    });
    expect(screen.getByText(/loan not found/i)).toBeInTheDocument();
  });

  it('renders the generic error state for non-404 failures', async () => {
    server.use(
      http.get('*/loans/:id', () => HttpResponse.json({ error: 'boom' }, { status: 500 })),
    );

    renderWithClient(<LoanDetailView id="l0000001-0000-0000-0000-000000000001" />);

    await waitFor(() => {
      const errorBlock = screen.getByTestId('loan-detail-error');
      expect(errorBlock).toBeInTheDocument();
      expect(errorBlock.getAttribute('data-not-found')).toBe('false');
    });
    expect(screen.getByText(/failed to load loan/i)).toBeInTheDocument();
  });

  it('renders the defensive generic error when the query is disabled (empty id)', () => {
    // An empty id disables the query → no loading, no error, no data → the
    // defensive `!data` branch renders a generic error.
    renderWithClient(<LoanDetailView id="" />);
    const errorBlock = screen.getByTestId('loan-detail-error');
    expect(errorBlock).toBeInTheDocument();
    expect(errorBlock.getAttribute('data-not-found')).toBe('false');
  });
});
