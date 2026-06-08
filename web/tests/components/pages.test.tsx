import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';

// Several pages mount components that call useRouter (delete dialogs, detail
// headers) or render charts via ResponsiveContainer. Stub both up-front.
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  usePathname: () => '/',
}));

vi.mock('recharts', async () => {
  const actual = await vi.importActual<typeof import('recharts')>('recharts');
  return {
    ...actual,
    ResponsiveContainer: ({ children }: { children: ReactNode }) => (
      <div style={{ width: 800, height: 300 }}>{children}</div>
    ),
  };
});

import AccountDetailPage from '@/app/accounts/[id]/page';
import AccountsPage from '@/app/accounts/page';
import BudgetsPage from '@/app/budgets/page';
import GoalDetailPage from '@/app/goals/[id]/page';
import GoalsPage from '@/app/goals/page';
import DashboardPage from '@/app/page';
import ReportsPage from '@/app/reports/page';
import CategoriesSettingsPage from '@/app/settings/categories/page';
import DataSettingsPage from '@/app/settings/data/page';
import FxRatesPage from '@/app/settings/fx-rates/page';
import SettingsPage from '@/app/settings/page';
import ImportPage from '@/app/transactions/import/page';
import TransactionsPage from '@/app/transactions/page';

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('App pages', () => {
  it('Dashboard page renders its header and widgets', async () => {
    renderWithClient(<DashboardPage />);
    expect(screen.getByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByText('Net worth')).toBeInTheDocument();
    });
  });

  it('Accounts page renders header + table', async () => {
    renderWithClient(<AccountsPage />);
    expect(screen.getByRole('heading', { name: 'Accounts' })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getAllByTestId('account-name-link').length).toBeGreaterThan(0);
    });
  });

  it('Budgets page renders header + table', async () => {
    renderWithClient(<BudgetsPage />);
    expect(screen.getByRole('heading', { name: 'Budgets' })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByText('Groceries')).toBeInTheDocument();
    });
  });

  it('Goals page renders header + table', async () => {
    renderWithClient(<GoalsPage />);
    expect(screen.getByRole('heading', { name: 'Goals' })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByText('Emergency fund')).toBeInTheDocument();
    });
  });

  it('Reports page renders header + tabs', async () => {
    renderWithClient(<ReportsPage />);
    expect(screen.getByRole('heading', { name: 'Reports' })).toBeInTheDocument();
  });

  it('Settings index page renders the three section link cards', () => {
    renderWithClient(<SettingsPage />);
    expect(screen.getByTestId('settings-link-categories')).toBeInTheDocument();
    expect(screen.getByTestId('settings-link-fx-rates')).toBeInTheDocument();
    expect(screen.getByTestId('settings-link-data')).toBeInTheDocument();
  });

  it('Categories settings page renders header + manager', async () => {
    renderWithClient(<CategoriesSettingsPage />);
    expect(screen.getByRole('heading', { name: 'Categories' })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByText('Groceries')).toBeInTheDocument();
    });
  });

  it('Data settings page renders export + import cards', () => {
    renderWithClient(<DataSettingsPage />);
    expect(screen.getByRole('heading', { name: 'Data' })).toBeInTheDocument();
  });

  it('FX rates page renders header + table', async () => {
    renderWithClient(<FxRatesPage />);
    expect(screen.getByRole('heading', { name: 'FX rates' })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getAllByText('MDL').length).toBeGreaterThan(0);
    });
  });

  it('Transactions page renders header + filters + table and reacts to a filter change', async () => {
    const user = userEvent.setup();
    renderWithClient(<TransactionsPage />);
    expect(screen.getByRole('heading', { name: 'Transactions' })).toBeInTheDocument();
    expect(screen.getByTestId('import-pdf-button')).toBeInTheDocument();

    // Changing a filter runs handleFilterChange (setFilterState + reset page).
    await waitFor(() => expect(screen.getByTestId('filter-direction-income')).toBeInTheDocument());
    await user.click(screen.getByTestId('filter-direction-income'));
    // The header subtitle reflects the active direction filter.
    await waitFor(() => {
      expect(screen.getByText(/→.*·\s*income/i)).toBeInTheDocument();
    });
  });

  it('Import page starts on the upload step and advances to preview after a parse', async () => {
    renderWithClient(<ImportPage />);
    expect(screen.getByRole('heading', { name: 'Import statement' })).toBeInTheDocument();
    expect(screen.getByText('← Back to transactions')).toBeInTheDocument();

    // Drive the upload step through a parse so the preview step renders.
    const file = new File(['%PDF-1.4'], 'statement.pdf', { type: 'application/pdf' });
    Object.defineProperty(file, 'size', { value: 2048 });
    const input = screen.getByTestId('import-file-input') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    fireEvent.change(input);

    const user = userEvent.setup();
    await waitFor(() => expect(screen.getByTestId('import-parse-button')).not.toBeDisabled());
    await user.click(screen.getByTestId('import-parse-button'));

    // ImportPreview (the preview step) renders the parsed rows.
    await waitFor(() => {
      expect(screen.getByText('LINELLA SRL CHISINAU')).toBeInTheDocument();
    });
  });

  it('Account detail page (async server component) resolves params and renders the view', async () => {
    const ui = await AccountDetailPage({
      params: Promise.resolve({ id: '44444444-4444-4444-4444-444444444444' }),
    });
    renderWithClient(ui);
    await waitFor(() => {
      expect(screen.getByTestId('account-detail-name')).toHaveTextContent('XTB');
    });
  });

  it('Goal detail page (async server component) resolves params and renders the view', async () => {
    const ui = await GoalDetailPage({
      params: Promise.resolve({ id: 'g0000001-0000-0000-0000-000000000001' }),
    });
    renderWithClient(ui);
    await waitFor(() => {
      expect(screen.getByText('Emergency fund')).toBeInTheDocument();
    });
  });
});
