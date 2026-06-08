import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { BalanceTrendCard } from '@/src/components/accounts/detail/balance-trend-card';
import { server } from '@/src/lib/mocks/server';
import type { AccountDetailDto } from '@/src/types/api';

vi.mock('recharts', async () => {
  const actual = await vi.importActual<typeof import('recharts')>('recharts');
  return {
    ...actual,
    ResponsiveContainer: ({ children }: { children: ReactNode }) => (
      <div style={{ width: 800, height: 300 }}>{children}</div>
    ),
  };
});

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

const usdAccount: AccountDetailDto = {
  id: '44444444-4444-4444-4444-444444444444',
  name: 'XTB',
  type: 'Brokerage',
  currency: 'USD',
  openingDate: '2024-09-10',
  isArchived: false,
  notes: null,
  balance: 1500,
  balanceMdl: 26250,
  initialCapital: 1000,
  allTime: {
    contributionsMdl: 0,
    withdrawalsMdl: 0,
    netPnLMdl: 0,
    contributionCount: 0,
    withdrawalCount: 0,
    adjustmentCount: 0,
    missingFxRate: false,
  },
  yearToDate: {
    contributionsMdl: 0,
    withdrawalsMdl: 0,
    netPnLMdl: 0,
    contributionCount: 0,
    withdrawalCount: 0,
    adjustmentCount: 0,
    missingFxRate: false,
  },
  firstActivityDate: '2024-09-10',
  lastActivityDate: '2025-05-15',
  realActivityCount: 2,
};

describe('BalanceTrendCard', () => {
  it('renders the chart with sr-only points and the dual-currency line for a USD account', async () => {
    renderWithClient(<BalanceTrendCard account={usdAccount} />);
    await waitFor(() => {
      expect(screen.getByTestId('balance-trend-chart')).toHaveAttribute('data-show-mdl', 'true');
    });
    expect(screen.getAllByTestId('balance-trend-point').length).toBeGreaterThan(0);
    // The USD points include a MDL-equivalent in the sr-only text.
    expect(screen.getByTestId('balance-trend-points').textContent).toContain('≈');
  });

  it('switches the interval (re-derives the window)', async () => {
    const user = userEvent.setup();
    renderWithClient(<BalanceTrendCard account={usdAccount} />);
    await waitFor(() => expect(screen.getByTestId('balance-trend-chart')).toBeInTheDocument());

    await user.click(screen.getByTestId('trend-interval'));
    await user.click(await screen.findByRole('option', { name: 'Daily' }));
    await waitFor(() => expect(screen.getByTestId('balance-trend-chart')).toBeInTheDocument());

    // Switch to Weekly too so the windowFor Weekly branch runs.
    await user.click(screen.getByTestId('trend-interval'));
    await user.click(await screen.findByRole('option', { name: 'Weekly' }));
    await waitFor(() => expect(screen.getByTestId('balance-trend-chart')).toBeInTheDocument());
  });

  it('shows the empty state when no points come back', async () => {
    server.use(http.get('*/reports/balance-over-time', () => HttpResponse.json([])));
    renderWithClient(<BalanceTrendCard account={usdAccount} />);
    expect(await screen.findByTestId('balance-trend-card-empty')).toBeInTheDocument();
  });

  it('shows the error state on failure', async () => {
    server.use(
      http.get('*/reports/balance-over-time', () => HttpResponse.json({}, { status: 500 })),
    );
    renderWithClient(<BalanceTrendCard account={usdAccount} />);
    expect(await screen.findByTestId('balance-trend-card-error')).toBeInTheDocument();
  });

  it('shows the missing-FX warning when a point lacks a rate', async () => {
    server.use(
      http.get('*/reports/balance-over-time', () =>
        HttpResponse.json([
          { asOf: '2026-05-01', balance: 1500, balanceMdl: null, missingFxRate: true },
        ]),
      ),
    );
    renderWithClient(<BalanceTrendCard account={usdAccount} />);
    expect(await screen.findByTestId('balance-trend-missing-fx')).toBeInTheDocument();
  });
});
