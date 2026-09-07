import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { PoolsTable } from '@/src/components/pools/pools-table';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import { formatMoney } from '@/src/lib/utils/currency';
import type { PoolDto } from '@/src/types/api';

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

describe('PoolsTable', () => {
  it('renders the seeded pool with its value, share and price per share', async () => {
    renderWithClient(<PoolsTable />);

    await waitFor(() => expect(screen.getAllByTestId('pool-row').length).toBe(1));
    expect(screen.getByTestId('pool-value').textContent).toBe(formatMoney(2500, 'USD'));
    expect(screen.getByTestId('pool-value-mdl').textContent).toBe(formatMoney(43750, 'MDL'));
    expect(screen.getByTestId('pool-owner-share').textContent).toBe('20.00%');
    expect(screen.getByTestId('pool-people').textContent).toBe('3');
    expect(screen.getByTestId('pool-nav').textContent).toBe(formatMoney(1.25, 'USD'));
  });

  it('links the pool name to its detail page and the account to the account page', async () => {
    renderWithClient(<PoolsTable />);
    await waitFor(() => expect(screen.getAllByTestId('pool-row').length).toBe(1));

    expect(screen.getByTestId('pool-name-link')).toHaveAttribute(
      'href',
      '/pools/aaaa0001-0000-4000-8000-000000000001',
    );
    expect(screen.getByRole('link', { name: 'Binanance' })).toHaveAttribute(
      'href',
      '/accounts/66666666-6666-6666-6666-666666666666',
    );
  });

  it('renders an unconvertible MDL equivalent as an em dash, never as 0', async () => {
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
    renderWithClient(<PoolsTable />);

    const cell = await screen.findByTestId('pool-value-mdl');
    expect(within(cell).getByTestId('pool-missing-fx')).toBeInTheDocument();
    expect(cell.textContent).toBe('—');
    // The specific failure this guards: a null MDL rendered as 0.00.
    expect(cell.textContent).not.toMatch(/0[.,]00/);
  });

  it('never invents a price per share for a pool with no shares outstanding', async () => {
    server.use(
      http.get('*/pools', () => HttpResponse.json([makePool({ totalUnits: 0, navPerUnit: null })])),
    );
    renderWithClient(<PoolsTable />);
    const cell = await screen.findByTestId('pool-nav');
    expect(cell.textContent).toBe('—');
  });

  it('hides archived pools until the toggle is flipped', async () => {
    const user = userEvent.setup();
    renderWithClient(<PoolsTable />);

    await waitFor(() => expect(screen.getAllByTestId('pool-row').length).toBe(1));
    expect(screen.queryByTestId('pool-archived-badge')).not.toBeInTheDocument();

    await user.click(screen.getByTestId('show-archived-pools-toggle'));

    await waitFor(() => expect(screen.getAllByTestId('pool-row').length).toBe(2));
    expect(screen.getByTestId('pool-archived-badge')).toBeInTheDocument();
  });

  it('offers no row menu on an archived pool — archiving is one-way', async () => {
    const user = userEvent.setup();
    renderWithClient(<PoolsTable />);
    await user.click(screen.getByTestId('show-archived-pools-toggle'));
    await waitFor(() => expect(screen.getAllByTestId('pool-row').length).toBe(2));

    // One menu across two rows: the live one has it, the archived one doesn't.
    expect(screen.getAllByTestId('pool-actions').length).toBe(1);
  });

  it('opens then closes the Archive dialog from the row menu', async () => {
    const user = userEvent.setup();
    renderWithClient(<PoolsTable />);
    await waitFor(() => expect(screen.getAllByTestId('pool-actions').length).toBe(1));

    await user.click(screen.getByTestId('pool-actions'));
    await user.click(await screen.findByTestId('archive-pool-action'));
    expect(await screen.findByTestId('archive-pool-dialog')).toBeInTheDocument();

    await user.keyboard('{Escape}');
    await waitFor(() =>
      expect(screen.queryByTestId('archive-pool-dialog')).not.toBeInTheDocument(),
    );
  });

  it('renders the empty hint when there are no pools', async () => {
    server.use(http.get('*/pools', () => HttpResponse.json([])));
    renderWithClient(<PoolsTable />);
    expect(await screen.findByTestId('pools-empty')).toBeInTheDocument();
  });

  it('renders the error state when the request fails', async () => {
    server.use(http.get('*/pools', () => HttpResponse.json({ error: 'boom' }, { status: 500 })));
    renderWithClient(<PoolsTable />);
    expect(await screen.findByText(/failed to load pools/i)).toBeInTheDocument();
  });

  it('renders skeleton rows while loading (no pool rows visible)', () => {
    server.use(http.get('*/pools', () => new Promise(() => {}) as unknown as Promise<Response>));
    renderWithClient(<PoolsTable />);
    expect(screen.queryAllByTestId('pool-row').length).toBe(0);
    expect(screen.getByTestId('pools-table')).toBeInTheDocument();
  });
});
