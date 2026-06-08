import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { MonthlySummaryCard } from '@/src/components/dashboard/monthly-summary-card';
import { server } from '@/src/lib/mocks/server';

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('MonthlySummaryCard', () => {
  it('shows the loading skeleton while the query is in flight', () => {
    server.use(
      http.get(
        '*/dashboard/summary',
        () => new Promise(() => {}), // never resolves
      ),
    );
    renderWithClient(<MonthlySummaryCard />);
    const card = screen.getByTestId('monthly-summary-card');
    expect(card.querySelector('[aria-label="Loading"]')).not.toBeNull();
  });

  it('renders an error message on a failed fetch', async () => {
    server.use(
      http.get('*/dashboard/summary', () => HttpResponse.json({ error: 'boom' }, { status: 500 })),
    );
    renderWithClient(<MonthlySummaryCard />);
    await waitFor(() => {
      expect(screen.getByTestId('monthly-summary-error')).toBeInTheDocument();
    });
  });

  it('renders income, expense, net and savings rate from the endpoint payload', async () => {
    server.use(
      http.get('*/dashboard/summary', () =>
        HttpResponse.json({
          month: '2026-05',
          income: 12000,
          expense: 8500,
          net: 3500,
          savingsRate: 0.28,
          transactionCount: 24,
          missingFxRate: false,
        }),
      ),
    );
    renderWithClient(<MonthlySummaryCard />);

    await waitFor(() => {
      // Locale formatting groups digits as 12.000 / 8.500 / 3.500 (ro-MD).
      expect(screen.getByTestId('monthly-summary-income').textContent ?? '').toMatch(/12\D?000/);
      expect(screen.getByTestId('monthly-summary-expense').textContent ?? '').toMatch(/8\D?500/);
      expect(screen.getByTestId('monthly-summary-net').textContent ?? '').toMatch(/3\D?500/);
    });
    expect(screen.getByTestId('monthly-summary-savings-rate').textContent).toMatch(/28\s?%/);
    expect(screen.getByTestId('monthly-summary-tx-count').textContent).toMatch(/24 transactions/);
    // No missing-FX warning when the flag is false.
    expect(screen.queryByTestId('monthly-summary-missing-fx')).not.toBeInTheDocument();
  });

  it('singularizes the transaction count when it equals 1', async () => {
    server.use(
      http.get('*/dashboard/summary', () =>
        HttpResponse.json({
          month: '2026-05',
          income: 1000,
          expense: 0,
          net: 1000,
          savingsRate: 1,
          transactionCount: 1,
          missingFxRate: false,
        }),
      ),
    );
    renderWithClient(<MonthlySummaryCard />);
    await waitFor(() => {
      expect(screen.getByTestId('monthly-summary-tx-count').textContent).toMatch(/^1 transaction$/);
    });
  });

  it('shows a muted dash for the savings rate when income is zero', async () => {
    server.use(
      http.get('*/dashboard/summary', () =>
        HttpResponse.json({
          month: '2026-05',
          income: 0,
          expense: 500,
          net: -500,
          savingsRate: 0,
          transactionCount: 2,
          missingFxRate: false,
        }),
      ),
    );
    renderWithClient(<MonthlySummaryCard />);
    await waitFor(() => {
      expect(screen.getByTestId('monthly-summary-savings-rate').textContent).toBe('—');
    });
  });

  it('renders the missing-FX warning line when the flag is true', async () => {
    server.use(
      http.get('*/dashboard/summary', () =>
        HttpResponse.json({
          month: '2026-05',
          income: 12000,
          expense: 8500,
          net: 3500,
          savingsRate: 3500 / 12000,
          transactionCount: 24,
          missingFxRate: true,
        }),
      ),
    );
    renderWithClient(<MonthlySummaryCard />);
    await waitFor(() => {
      expect(screen.getByTestId('monthly-summary-missing-fx')).toBeInTheDocument();
    });
  });

  it('renders a neutral (text-foreground) net cell when net is exactly zero', async () => {
    server.use(
      http.get('*/dashboard/summary', () =>
        HttpResponse.json({
          month: '2026-05',
          income: 5000,
          expense: 5000,
          net: 0,
          savingsRate: 0,
          transactionCount: 4,
          missingFxRate: false,
        }),
      ),
    );
    renderWithClient(<MonthlySummaryCard />);
    await waitFor(() => {
      const net = screen.getByTestId('monthly-summary-net');
      // The zero-net branch uses text-foreground rather than emerald/red.
      expect(net.className).toMatch(/text-foreground/);
    });
  });
});
