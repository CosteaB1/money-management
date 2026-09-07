import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { PoolsSummary } from '@/src/components/pools/pools-summary';
import { server } from '@/src/lib/mocks/server';
import { formatMoney } from '@/src/lib/utils/currency';
import type { PoolDto } from '@/src/types/api';

vi.mock('next/navigation', () => ({ usePathname: () => '/pools' }));

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

function makePool(overrides: Partial<PoolDto>): PoolDto {
  return {
    id: overrides.id ?? crypto.randomUUID(),
    accountId: '66666666-6666-6666-6666-666666666666',
    accountName: 'Binanance',
    name: 'A pool',
    currency: 'USD',
    inceptionDate: '2026-08-01',
    notes: null,
    isArchived: false,
    accountBalance: 2500,
    unpaidDistributionCash: 0,
    unpaidDistributionCount: 0,
    poolValue: 2500,
    poolValueMdl: 43750,
    totalUnits: 2000,
    navPerUnit: 1.25,
    participantCount: 3,
    ownerFraction: 0.2,
    outsideCapital: 2000,
    outsideCapitalMdl: 35000,
    missingFxRate: false,
    ...overrides,
  };
}

describe('PoolsSummary', () => {
  it('splits the pool into your share and everyone else, in MDL', async () => {
    renderWithClient(<PoolsSummary />);

    // 43,750 pool − 35,000 theirs = 8,750 yours, and the two tiles must add
    // up to the third exactly.
    await waitFor(() => {
      expect(screen.getByTestId('your-share-amount').textContent).toBe(formatMoney(8750, 'MDL'));
    });
    expect(screen.getByTestId('outside-capital-amount').textContent).toBe(
      formatMoney(35000, 'MDL'),
    );
    expect(screen.getByTestId('pool-value-amount').textContent).toBe(formatMoney(43750, 'MDL'));
  });

  it('shows the owner percentage alongside the value', async () => {
    renderWithClient(<PoolsSummary />);
    await waitFor(() => {
      expect(screen.getByTestId('your-share-percent')).toHaveTextContent('20.00% of the pool');
    });
  });

  it('shows the unpaid-payouts tile only while a closed payout has not been sent', async () => {
    renderWithClient(<PoolsSummary />);

    // The seeded pool carries one closed-but-unpaid payout of USD 100.
    await waitFor(() => {
      expect(screen.getByTestId('unpaid-payouts-amount').textContent).toBe(formatMoney(100, 'USD'));
    });
    expect(screen.getByTestId('unpaid-payouts-count')).toHaveTextContent('1 payout owed');
  });

  it('hides the unpaid tile entirely when nothing is owed', async () => {
    server.use(
      http.get('*/pools', () =>
        HttpResponse.json([makePool({ unpaidDistributionCash: 0, unpaidDistributionCount: 0 })]),
      ),
    );
    renderWithClient(<PoolsSummary />);
    await screen.findByTestId('your-share-amount');
    expect(screen.queryByTestId('unpaid-payouts-card')).not.toBeInTheDocument();
  });

  it('says "Multiple currencies" rather than summing two of them into a fiction', async () => {
    server.use(
      http.get('*/pools', () =>
        HttpResponse.json([
          makePool({
            id: 'a',
            currency: 'USD',
            unpaidDistributionCash: 100,
            unpaidDistributionCount: 1,
          }),
          makePool({
            id: 'b',
            currency: 'EUR',
            unpaidDistributionCash: 50,
            unpaidDistributionCount: 1,
          }),
        ]),
      ),
    );
    renderWithClient(<PoolsSummary />);
    await waitFor(() => {
      expect(screen.getByTestId('unpaid-payouts-amount')).toHaveTextContent('Multiple currencies');
    });
    expect(screen.getByTestId('unpaid-payouts-count')).toHaveTextContent('2 payouts owed');
  });

  it('drops an unconvertible pool from the totals and says so, never showing it as 0', async () => {
    server.use(
      http.get('*/pools', () =>
        HttpResponse.json([
          makePool({ id: 'ok' }),
          makePool({
            id: 'no-rate',
            currency: 'GBP',
            poolValueMdl: null,
            outsideCapitalMdl: null,
            missingFxRate: true,
          }),
        ]),
      ),
    );
    renderWithClient(<PoolsSummary />);

    await waitFor(() => {
      expect(screen.getByTestId('pools-missing-fx')).toHaveTextContent(
        '1 pool missing FX rate — totals are incomplete',
      );
    });
    // The convertible pool's figures stand alone; the unconvertible one
    // contributes nothing rather than a zero.
    expect(screen.getByTestId('pool-value-amount').textContent).toBe(formatMoney(43750, 'MDL'));
  });

  it('renders an em dash rather than 0% when nothing can be weighted', async () => {
    server.use(
      http.get('*/pools', () =>
        HttpResponse.json([
          makePool({
            currency: 'GBP',
            poolValueMdl: null,
            outsideCapitalMdl: null,
            missingFxRate: true,
          }),
        ]),
      ),
    );
    renderWithClient(<PoolsSummary />);
    await waitFor(() => {
      expect(screen.getByTestId('your-share-percent')).toHaveTextContent('— of the pool');
    });
  });

  it('ignores archived pools even if the list hands them over', async () => {
    server.use(
      http.get('*/pools', () =>
        HttpResponse.json([makePool({ id: 'live' }), makePool({ id: 'gone', isArchived: true })]),
      ),
    );
    renderWithClient(<PoolsSummary />);
    await waitFor(() => {
      expect(screen.getByTestId('pool-value-amount').textContent).toBe(formatMoney(43750, 'MDL'));
    });
  });

  it('shows a loading state while the request is in flight', async () => {
    server.use(
      http.get('*/pools', async () => {
        await new Promise((r) => setTimeout(r, 40));
        return HttpResponse.json([makePool({})]);
      }),
    );
    renderWithClient(<PoolsSummary />);
    expect(screen.getAllByRole('status', { name: 'Loading' }).length).toBeGreaterThan(0);
    await screen.findByTestId('your-share-amount');
  });

  it('shows an error message on failure', async () => {
    server.use(http.get('*/pools', () => HttpResponse.json({}, { status: 500 })));
    renderWithClient(<PoolsSummary />);
    expect((await screen.findAllByText('Failed to load.')).length).toBeGreaterThan(0);
  });
});
