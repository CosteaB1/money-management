import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { AccountsTable } from '@/src/components/accounts/accounts-table';
import { AccountDetailHeader } from '@/src/components/accounts/detail/account-detail-header';
import { PerformanceCard } from '@/src/components/accounts/detail/performance-card';
import { TotalAssetsCard } from '@/src/components/dashboard/total-assets-card';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import { formatMoney } from '@/src/lib/utils/currency';
import type { AccountDetailDto, AccountDto } from '@/src/types/api';

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  usePathname: () => '/accounts',
}));

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

const pooledAccount: AccountDto = {
  id: '66666666-6666-6666-6666-666666666666',
  name: 'Binanance',
  type: 'CryptoExchange',
  currency: 'USD',
  openingDate: '2024-01-01',
  isArchived: false,
  isPooled: true,
  notes: null,
  balance: 2600,
  balanceMdl: 45500,
};

const pooledDetail: AccountDetailDto = {
  id: pooledAccount.id,
  name: pooledAccount.name,
  type: pooledAccount.type,
  currency: pooledAccount.currency,
  openingDate: pooledAccount.openingDate,
  isArchived: false,
  isPooled: true,
  notes: null,
  balance: 2600,
  balanceMdl: 45500,
  initialCapital: 0,
  allTime: {
    contributionsMdl: 800,
    withdrawalsMdl: 0,
    netPnLMdl: 1500,
    contributionCount: 1,
    withdrawalCount: 0,
    adjustmentCount: 3,
    missingFxRate: false,
  },
  yearToDate: {
    contributionsMdl: 800,
    withdrawalsMdl: 0,
    netPnLMdl: 1500,
    contributionCount: 1,
    withdrawalCount: 0,
    adjustmentCount: 3,
    missingFxRate: false,
  },
  firstActivityDate: '2026-08-01',
  lastActivityDate: '2026-08-31',
  realActivityCount: 4,
};

describe('the Pooled badge on /accounts', () => {
  it('badges a pooled row while still showing its FULL balance', async () => {
    server.use(http.get('*/accounts', () => HttpResponse.json([pooledAccount])));
    renderWithClient(<AccountsTable />);

    const row = await screen.findByTestId('account-row');
    expect(within(row).getByTestId('account-pooled-badge')).toHaveTextContent('Pooled');
    // The read-side divergence, decided explicitly: /accounts keeps showing the
    // whole balance because the account really does hold that money.
    // textContent, not toHaveTextContent: formatMoney emits a non-breaking
    // space that jest-dom's whitespace normalisation would strip on one side
    // of the comparison only.
    expect(row.textContent).toContain(formatMoney(2600, 'USD'));
    expect(within(row).getByTestId('account-mdl-eq').textContent).toBe(formatMoney(45500, 'MDL'));
  });

  it('leaves ordinary accounts unbadged', async () => {
    renderWithClient(<AccountsTable />);
    await waitFor(() => expect(screen.getAllByTestId('account-row').length).toBeGreaterThan(0));
    expect(screen.queryByTestId('account-pooled-badge')).not.toBeInTheDocument();
  });
});

describe('the Pooled badge on the account detail header', () => {
  it('badges the header and says the balance shown is gross', () => {
    renderWithClient(<AccountDetailHeader account={pooledDetail} />);

    expect(screen.getByTestId('account-detail-pooled')).toHaveTextContent('Pooled');
    const note = screen.getByTestId('account-detail-pooled-note');
    expect(note).toHaveTextContent(/Part of this balance belongs to other people/i);
    expect(note).toHaveTextContent(/your net worth only counts your share/i);
    expect(screen.getByRole('link', { name: /Part of this balance/ })).toHaveAttribute(
      'href',
      '/pools',
    );
    // Still the full account value, unmodified.
    expect(screen.getByTestId('account-detail-balance').textContent).toBe(formatMoney(2600, 'USD'));
  });

  it('says nothing on an ordinary account', () => {
    renderWithClient(<AccountDetailHeader account={{ ...pooledDetail, isPooled: false }} />);
    expect(screen.queryByTestId('account-detail-pooled')).not.toBeInTheDocument();
    expect(screen.queryByTestId('account-detail-pooled-note')).not.toBeInTheDocument();
  });
});

describe('the Performance card on a pooled account', () => {
  it('labels the three KPIs as the owner-only figures they are', () => {
    render(<PerformanceCard account={pooledDetail} />);

    expect(screen.getByTestId('perf-owner-only-badge')).toHaveTextContent('Your share only');
    const note = screen.getByTestId('perf-pooled-note');
    expect(note).toHaveTextContent(/count only your side of it/i);
    // …while the current value line stays gross, and says so.
    expect(note).toHaveTextContent(/current\s+value is the whole account/i);
    expect(screen.getByTestId('perf-current-mdl').textContent).toBe(formatMoney(45500, 'MDL'));
  });

  it('leaves an unpooled account card exactly as it was', () => {
    render(<PerformanceCard account={{ ...pooledDetail, isPooled: false }} />);
    expect(screen.queryByTestId('perf-owner-only-badge')).not.toBeInTheDocument();
    expect(screen.queryByTestId('perf-pooled-note')).not.toBeInTheDocument();
  });
});

describe('the Total assets tile sub-line', () => {
  function netWorthBody(overrides: Record<string, number> = {}) {
    return {
      grossAssetsMdl: 48100,
      externalLiabilitiesMdl: 57600,
      externalAssetsMdl: 2000,
      netWorthMdl: -7500,
      accountsMissingFxRate: 0,
      loansMissingFxRate: 0,
      outsideCapitalMdl: 0,
      ...overrides,
    };
  }

  it('spells out how much of the accounts is not the user', async () => {
    server.use(
      http.get('*/dashboard/net-worth', () =>
        HttpResponse.json(netWorthBody({ outsideCapitalMdl: 35000 })),
      ),
    );
    renderWithClient(<TotalAssetsCard />);

    const line = await screen.findByTestId('total-assets-outside-capital');
    expect(line.textContent).toContain(formatMoney(35000, 'MDL'));
    expect(line).toHaveTextContent(/already left out of this total/i);
    expect(within(line).getByRole('link')).toHaveAttribute('href', '/pools');
    // The headline is untouched: outside capital was never in gross assets.
    expect(screen.getByTestId('total-assets-amount').textContent).toBe(formatMoney(48100, 'MDL'));
  });

  it('stays absent for a user who has never pooled anything', async () => {
    server.use(http.get('*/dashboard/net-worth', () => HttpResponse.json(netWorthBody())));
    renderWithClient(<TotalAssetsCard />);
    await screen.findByTestId('total-assets-amount');
    expect(screen.queryByTestId('total-assets-outside-capital')).not.toBeInTheDocument();
  });

  it('treats a backend that omits the field as zero rather than blowing up', async () => {
    const { outsideCapitalMdl, ...withoutField } = netWorthBody();
    void outsideCapitalMdl;
    server.use(http.get('*/dashboard/net-worth', () => HttpResponse.json(withoutField)));
    renderWithClient(<TotalAssetsCard />);
    await screen.findByTestId('total-assets-amount');
    expect(screen.queryByTestId('total-assets-outside-capital')).not.toBeInTheDocument();
  });
});
