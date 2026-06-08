import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { BudgetProgress } from '@/src/components/dashboard/budget-progress';
import { server } from '@/src/lib/mocks/server';
import type { BudgetDto } from '@/src/types/api';

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

function makeBudget(overrides: Partial<BudgetDto>): BudgetDto {
  return {
    id: overrides.id ?? crypto.randomUUID(),
    categoryId: overrides.categoryId ?? crypto.randomUUID(),
    categoryName: overrides.categoryName ?? 'Groceries',
    monthlyLimit: overrides.monthlyLimit ?? 1000,
    spent: overrides.spent ?? 0,
    remaining: overrides.remaining ?? 1000,
    status: overrides.status ?? 'OnTrack',
    year: overrides.year ?? 2026,
    month: overrides.month ?? 5,
  };
}

describe('BudgetProgress dashboard widget', () => {
  it('renders the empty hint with a link to /budgets when no budgets exist', async () => {
    server.use(http.get('*/budgets', () => HttpResponse.json([])));

    renderWithClient(<BudgetProgress />);

    await waitFor(() => {
      expect(screen.getByTestId('budget-progress-empty')).toBeInTheDocument();
    });

    const link = screen.getByRole('link', { name: /add one in \/budgets/i });
    expect(link).toHaveAttribute('href', '/budgets');
  });

  it('renders an error message when the request fails', async () => {
    server.use(http.get('*/budgets', () => HttpResponse.json({ error: 'boom' }, { status: 500 })));

    renderWithClient(<BudgetProgress />);

    await waitFor(() => {
      expect(screen.getByTestId('budget-progress-error')).toBeInTheDocument();
    });
  });

  it('shows up to 5 rows sorted by spend percentage (highest first)', async () => {
    // 6 budgets — only the top 5 by spend% should render.
    const six: BudgetDto[] = [
      makeBudget({
        id: 'low',
        categoryName: 'Low',
        monthlyLimit: 1000,
        spent: 100,
        status: 'OnTrack',
      }),
      makeBudget({
        id: 'mid',
        categoryName: 'Mid',
        monthlyLimit: 1000,
        spent: 500,
        status: 'OnTrack',
      }),
      makeBudget({
        id: 'warn',
        categoryName: 'Warn',
        monthlyLimit: 1000,
        spent: 900,
        status: 'Warning',
      }),
      makeBudget({
        id: 'over1',
        categoryName: 'Over1',
        monthlyLimit: 1000,
        spent: 1100,
        status: 'Over',
      }),
      makeBudget({
        id: 'over2',
        categoryName: 'Over2',
        monthlyLimit: 1000,
        spent: 1500,
        status: 'Over',
      }),
      makeBudget({
        id: 'untouched',
        categoryName: 'Untouched',
        monthlyLimit: 1000,
        spent: 0,
        status: 'OnTrack',
      }),
    ];

    server.use(http.get('*/budgets', () => HttpResponse.json(six)));

    renderWithClient(<BudgetProgress />);

    await waitFor(() => {
      const rows = screen.getAllByTestId('budget-progress-row');
      expect(rows.length).toBe(5);
    });

    const labels = screen.getAllByTestId('budget-progress-row').map((r) => r.textContent ?? '');

    // Highest spend% comes first, lowest of the top 5 last. "Untouched" (0%)
    // is dropped because only 5 rows are shown.
    expect(labels[0]).toContain('Over2');
    expect(labels[1]).toContain('Over1');
    expect(labels[2]).toContain('Warn');
    expect(labels[3]).toContain('Mid');
    expect(labels[4]).toContain('Low');
    expect(labels.join(' ')).not.toContain('Untouched');
  });

  it('shows the "View all" link when budgets exist', async () => {
    renderWithClient(<BudgetProgress />);

    await waitFor(() => {
      const link = screen.getByTestId('budget-progress-view-all');
      expect(link).toHaveAttribute('href', '/budgets');
    });
  });

  it('renders skeletons (and no rows) while loading', async () => {
    // Hold the request open — we only assert the loading-branch render.
    server.use(http.get('*/budgets', () => new Promise(() => {}) as unknown as Promise<Response>));

    renderWithClient(<BudgetProgress />);

    // No budget rows yet.
    expect(screen.queryAllByTestId('budget-progress-row').length).toBe(0);
    // The card itself is mounted.
    expect(screen.getByTestId('budget-progress-card')).toBeInTheDocument();
  });
});
