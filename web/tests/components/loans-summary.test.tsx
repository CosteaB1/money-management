import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { LoansSummary } from '@/src/components/loans/loans-summary';
import { server } from '@/src/lib/mocks/server';
import { formatMoney } from '@/src/lib/utils/currency';
import type { LoanDto } from '@/src/types/api';

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

function makeLoan(overrides: Partial<LoanDto>): LoanDto {
  return {
    id: overrides.id ?? crypto.randomUUID(),
    direction: overrides.direction ?? 'Given',
    counterparty: overrides.counterparty ?? 'Somebody',
    principal: overrides.principal ?? 1000,
    currency: overrides.currency ?? 'MDL',
    loanDate: overrides.loanDate ?? '2026-01-01',
    totalRepaid: overrides.totalRepaid ?? 0,
    outstanding: overrides.outstanding ?? 1000,
    outstandingMdl: overrides.outstandingMdl !== undefined ? overrides.outstandingMdl : 1000,
    missingFxRate: overrides.missingFxRate ?? false,
    status: overrides.status ?? 'Active',
    paymentCount: overrides.paymentCount ?? 0,
    isAccountLinked: overrides.isAccountLinked ?? false,
    notes: overrides.notes ?? null,
  };
}

describe('LoansSummary', () => {
  it('sums Given loans into "Owed to me" and Received loans into "I owe" (MDL)', async () => {
    renderWithClient(<LoansSummary />);

    // Seed: Given → Ion 2000 MDL + Maria 0 MDL; Received → Parents 57600 MDL.
    await waitFor(() => {
      expect(screen.getByTestId('owed-to-me-amount')).toBeInTheDocument();
    });
    expect(screen.getByTestId('owed-to-me-amount').textContent).toBe(formatMoney(2000, 'MDL'));
    expect(screen.getByTestId('i-owe-amount').textContent).toBe(formatMoney(57600, 'MDL'));

    // No missing-FX warning on either tile with the fully-convertible seed.
    expect(screen.queryByTestId('owed-to-me-missing-fx')).not.toBeInTheDocument();
    expect(screen.queryByTestId('i-owe-missing-fx')).not.toBeInTheDocument();
  });

  it('flags the tile whose bucket has loans missing an FX rate and excludes them from the sum', async () => {
    server.use(
      http.get('*/loans', () =>
        HttpResponse.json([
          makeLoan({ direction: 'Given', outstanding: 2000, outstandingMdl: 2000 }),
          makeLoan({
            direction: 'Given',
            currency: 'GBP',
            outstanding: 500,
            outstandingMdl: null,
            missingFxRate: true,
          }),
          makeLoan({ direction: 'Received', outstanding: 300, outstandingMdl: 300 }),
        ]),
      ),
    );

    renderWithClient(<LoansSummary />);

    await waitFor(() => {
      expect(screen.getByTestId('owed-to-me-missing-fx')).toBeInTheDocument();
    });
    // Unconvertible loan contributes 0 — the partial total is flagged, not wrong.
    expect(screen.getByTestId('owed-to-me-amount').textContent).toBe(formatMoney(2000, 'MDL'));
    expect(screen.getByTestId('owed-to-me-missing-fx')).toHaveTextContent('1 loan missing FX rate');
    // The Received bucket is fully convertible — no warning there.
    expect(screen.queryByTestId('i-owe-missing-fx')).not.toBeInTheDocument();
    expect(screen.getByTestId('i-owe-amount').textContent).toBe(formatMoney(300, 'MDL'));
  });

  it('pluralizes the missing-FX warning', async () => {
    server.use(
      http.get('*/loans', () =>
        HttpResponse.json([
          makeLoan({
            direction: 'Received',
            currency: 'GBP',
            outstandingMdl: null,
            missingFxRate: true,
          }),
          makeLoan({
            direction: 'Received',
            currency: 'USD',
            outstandingMdl: null,
            missingFxRate: true,
          }),
        ]),
      ),
    );

    renderWithClient(<LoansSummary />);

    await waitFor(() => {
      expect(screen.getByTestId('i-owe-missing-fx')).toHaveTextContent('2 loans missing FX rate');
    });
  });

  it('renders loading placeholders while the fetch is pending', () => {
    server.use(http.get('*/loans', () => new Promise(() => {}) as unknown as Promise<Response>));

    renderWithClient(<LoansSummary />);

    const tiles = screen.getByTestId('loans-summary');
    expect(within(tiles).getAllByRole('status', { name: 'Loading' }).length).toBe(2);
  });

  it('renders the error state on both tiles when the request fails', async () => {
    server.use(http.get('*/loans', () => HttpResponse.json({ error: 'boom' }, { status: 500 })));

    renderWithClient(<LoansSummary />);

    await waitFor(() => {
      expect(screen.getAllByText('Failed to load.').length).toBe(2);
    });
  });
});
