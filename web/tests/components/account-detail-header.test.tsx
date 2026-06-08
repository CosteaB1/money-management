import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { AccountDetailHeader } from '@/src/components/accounts/detail/account-detail-header';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import type { AccountDetailDto } from '@/src/types/api';

// The header calls `useRouter().push('/accounts')` from the delete dialog's
// onDeleted. The default next/navigation stub blows up without an App-Router
// context in jsdom, so mock the hook to a noop.
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

const baseTotals = {
  contributionsMdl: 0,
  withdrawalsMdl: 0,
  netPnLMdl: 0,
  contributionCount: 0,
  withdrawalCount: 0,
  adjustmentCount: 0,
  missingFxRate: false,
};

const brokerageAccount: AccountDetailDto = {
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
  allTime: baseTotals,
  yearToDate: baseTotals,
  firstActivityDate: null,
  lastActivityDate: null,
  realActivityCount: 0,
};

const cashAccount: AccountDetailDto = {
  ...brokerageAccount,
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Cash Wallet',
  type: 'Cash',
  currency: 'MDL',
  balance: 500,
  balanceMdl: 500,
  initialCapital: 500,
};

describe('AccountDetailHeader', () => {
  it('renders name, type badge, currency badge, and the native balance', () => {
    renderWithClient(<AccountDetailHeader account={brokerageAccount} />);

    expect(screen.getByTestId('account-detail-name')).toHaveTextContent('XTB');
    expect(screen.getByTestId('account-detail-type')).toHaveTextContent(/brokerage/i);
    expect(screen.getByTestId('account-detail-currency')).toHaveTextContent('USD');
    expect(screen.getByTestId('account-detail-balance')).toHaveTextContent(/1[.\s]?500/);
  });

  it('shows the MDL-equivalent balance only when currency != MDL', () => {
    const { rerender } = renderWithClient(<AccountDetailHeader account={brokerageAccount} />);
    expect(screen.getByTestId('account-detail-balance-mdl')).toBeInTheDocument();

    rerender(
      <QueryClientProvider client={new QueryClient()}>
        <AccountDetailHeader account={cashAccount} />
      </QueryClientProvider>,
    );
    expect(screen.queryByTestId('account-detail-balance-mdl')).not.toBeInTheDocument();
  });

  it('surfaces the Archived badge when the account is archived', () => {
    renderWithClient(<AccountDetailHeader account={{ ...brokerageAccount, isArchived: true }} />);
    expect(screen.getByTestId('account-detail-archived')).toBeInTheDocument();
    // Archived accounts swap the active-state action group for a single
    // Unarchive control — the active-state actions must be gone.
    expect(screen.queryByTestId('account-detail-update-balance')).not.toBeInTheDocument();
    expect(screen.queryByTestId('account-detail-archive')).not.toBeInTheDocument();
  });

  it('shows the Unarchive button only when the account is archived', () => {
    const { rerender } = renderWithClient(
      <AccountDetailHeader account={{ ...brokerageAccount, isArchived: true }} />,
    );
    expect(screen.getByTestId('account-detail-unarchive')).toBeInTheDocument();

    // A non-archived account renders no Unarchive control.
    rerender(
      <QueryClientProvider client={new QueryClient()}>
        <AccountDetailHeader account={brokerageAccount} />
      </QueryClientProvider>,
    );
    expect(screen.queryByTestId('account-detail-unarchive')).not.toBeInTheDocument();
  });

  it('hides Update balance for non-adjustable account types', () => {
    renderWithClient(<AccountDetailHeader account={cashAccount} />);
    expect(screen.queryByTestId('account-detail-update-balance')).not.toBeInTheDocument();
    // Non-adjustable types still expose the actions strip (Archive / Delete)
    // but no balance-change entry point.
    expect(screen.getByTestId('account-detail-actions')).toBeInTheDocument();
    expect(screen.getByTestId('account-detail-archive')).toBeInTheDocument();
  });

  it('shows Update balance for adjustable account types', () => {
    renderWithClient(<AccountDetailHeader account={brokerageAccount} />);
    expect(screen.getByTestId('account-detail-update-balance')).toBeInTheDocument();
  });

  it('renders an accessible Back-to-accounts link', () => {
    renderWithClient(<AccountDetailHeader account={brokerageAccount} />);
    const link = screen.getByTestId('account-detail-back');
    expect(link).toHaveAttribute('href', '/accounts');
    expect(link).toHaveAccessibleName(/back to accounts/i);
  });

  it('renders the Delete-permanently button for an active account', () => {
    renderWithClient(<AccountDetailHeader account={brokerageAccount} />);
    expect(screen.getByTestId('account-detail-delete')).toBeInTheDocument();
  });

  it('renders the Edit button for an active account and opens the edit dialog', async () => {
    const user = userEvent.setup();
    renderWithClient(<AccountDetailHeader account={brokerageAccount} />);

    const editBtn = screen.getByTestId('account-detail-edit');
    expect(editBtn).toBeInTheDocument();
    await user.click(editBtn);

    expect(await screen.findByTestId('edit-account-dialog')).toBeInTheDocument();
    // The dialog is seeded from the header's lighter accountForDialogs object.
    expect(screen.getByTestId('account-edit-name-input')).toHaveValue('XTB');
  });

  it('hides the Edit button for an archived account', () => {
    renderWithClient(<AccountDetailHeader account={{ ...brokerageAccount, isArchived: true }} />);
    expect(screen.queryByTestId('account-detail-edit')).not.toBeInTheDocument();
  });

  it('renders the Delete-permanently button for an archived account too', () => {
    renderWithClient(<AccountDetailHeader account={{ ...brokerageAccount, isArchived: true }} />);
    expect(screen.getByTestId('account-detail-delete')).toBeInTheDocument();
  });

  it('shows the New-transfer action for CryptoExchange accounts and opens the dialog', async () => {
    const user = userEvent.setup();
    const cryptoAccount: AccountDetailDto = {
      ...brokerageAccount,
      id: '66666666-6666-6666-6666-666666666666',
      name: 'Binance',
      type: 'CryptoExchange',
    };
    renderWithClient(<AccountDetailHeader account={cryptoAccount} />);
    const transferBtn = screen.getByTestId('account-detail-new-transfer');
    expect(transferBtn).toBeInTheDocument();
    await user.click(transferBtn);
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
  });

  it('renders an em dash in the MDL-eq line when the rate is missing', () => {
    renderWithClient(<AccountDetailHeader account={{ ...brokerageAccount, balanceMdl: null }} />);
    const mdl = screen.getByTestId('account-detail-balance-mdl');
    expect(mdl).toHaveTextContent('≈ —');
  });

  it('archives an active account from the header and toasts success', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider
        client={
          new QueryClient({
            defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
          })
        }
      >
        <AccountDetailHeader account={brokerageAccount} />
        <Toaster />
      </QueryClientProvider>,
    );
    await user.click(screen.getByTestId('account-detail-archive'));
    expect(await screen.findByText('Archived "XTB"')).toBeInTheDocument();
  });

  it('unarchives an archived account from the header and toasts success', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider
        client={
          new QueryClient({
            defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
          })
        }
      >
        <AccountDetailHeader account={{ ...brokerageAccount, isArchived: true }} />
        <Toaster />
      </QueryClientProvider>,
    );
    await user.click(screen.getByTestId('account-detail-unarchive'));
    expect(await screen.findByText('Unarchived "XTB"')).toBeInTheDocument();
  });

  it('toasts an error when archiving from the header fails', async () => {
    server.use(
      http.delete('*/accounts/:id', () => HttpResponse.json({ detail: 'busy' }, { status: 500 })),
    );
    const user = userEvent.setup();
    render(
      <QueryClientProvider
        client={
          new QueryClient({
            defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
          })
        }
      >
        <AccountDetailHeader account={brokerageAccount} />
        <Toaster />
      </QueryClientProvider>,
    );
    await user.click(screen.getByTestId('account-detail-archive'));
    expect(await screen.findByText('busy')).toBeInTheDocument();
  });

  it('toasts an error when unarchiving from the header fails', async () => {
    server.use(
      http.post('*/accounts/:id/unarchive', () =>
        HttpResponse.json({ detail: 'locked' }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    render(
      <QueryClientProvider
        client={
          new QueryClient({
            defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
          })
        }
      >
        <AccountDetailHeader account={{ ...brokerageAccount, isArchived: true }} />
        <Toaster />
      </QueryClientProvider>,
    );
    await user.click(screen.getByTestId('account-detail-unarchive'));
    expect(await screen.findByText('locked')).toBeInTheDocument();
  });
});
