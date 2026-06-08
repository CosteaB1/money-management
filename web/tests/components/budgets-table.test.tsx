import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { BudgetsTable } from '@/src/components/budgets/budgets-table';
import { server } from '@/src/lib/mocks/server';

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('BudgetsTable', () => {
  it('renders three seeded budget rows with progress bars and status pills', async () => {
    renderWithClient(<BudgetsTable />);

    await waitFor(() => {
      const rows = screen.getAllByTestId('budget-row');
      expect(rows.length).toBe(3);
    });

    // Status pills surface the three seeded buckets.
    const pills = screen.getAllByTestId('budget-status-pill');
    const pillTexts = pills.map((p) => p.textContent);
    expect(pillTexts).toContain('On track');
    expect(pillTexts).toContain('Warning');
    expect(pillTexts).toContain('Over');

    // Each row has a progress bar carrying the same status as the row.
    const bars = screen.getAllByTestId('budget-progress-bar');
    expect(bars.length).toBe(3);
    for (const bar of bars) {
      const status = bar.getAttribute('data-status');
      expect(status).toMatch(/OnTrack|Warning|Over/);
      // Inline style sets a width percentage — assert a width is present.
      expect(bar.getAttribute('style') ?? '').toMatch(/width:\s*\d/);
    }
  });

  it('caps progress width visually at 120% on over-budget rows', async () => {
    renderWithClient(<BudgetsTable />);

    await waitFor(() => {
      expect(screen.getAllByTestId('budget-row').length).toBe(3);
    });

    // The Transport row is seeded at 1450 / 1000 = 145% — should be capped
    // at 120% by the bar but keep the "Over" status pill.
    const rows = screen.getAllByTestId('budget-row');
    const overRow = rows.find((r) => r.getAttribute('data-status') === 'Over');
    expect(overRow).toBeDefined();
    const bar = overRow?.querySelector('[data-testid="budget-progress-bar"]');
    expect(bar).toBeTruthy();
    const style = bar?.getAttribute('style') ?? '';
    // 120% cap → style: "width: 120%"
    expect(style).toMatch(/width:\s*120%/);

    const pillsInOverRow = overRow?.querySelectorAll('[data-testid="budget-status-pill"]');
    expect(pillsInOverRow?.[0]?.textContent).toBe('Over');
  });

  it('renders the empty hint when no budgets exist', async () => {
    server.use(http.get('*/budgets', () => HttpResponse.json([])));

    renderWithClient(<BudgetsTable />);

    await waitFor(() => {
      expect(screen.getByText(/no budgets yet/i)).toBeInTheDocument();
    });
  });

  it('renders the error state when the request fails', async () => {
    server.use(http.get('*/budgets', () => HttpResponse.json({ error: 'boom' }, { status: 500 })));

    renderWithClient(<BudgetsTable />);

    await waitFor(() => {
      expect(screen.getByText(/failed to load budgets/i)).toBeInTheDocument();
    });
  });

  it('renders skeleton rows while loading (no budget rows visible)', async () => {
    // Hold the request open for the duration of the assertion — we don't
    // need it to ever resolve, we only care that the loading branch is
    // what's painted before the first response arrives.
    server.use(http.get('*/budgets', () => new Promise(() => {}) as unknown as Promise<Response>));

    renderWithClient(<BudgetsTable />);

    // Skeletons paint animated bars — no actual budget rows yet.
    expect(screen.queryAllByTestId('budget-row').length).toBe(0);
    // And the table itself is rendered (header is up).
    expect(screen.getByTestId('budgets-table')).toBeInTheDocument();
  });

  it('opens the Edit dialog from a row menu', async () => {
    const user = userEvent.setup();
    renderWithClient(<BudgetsTable />);
    await waitFor(() => expect(screen.getAllByTestId('budget-actions').length).toBe(3));
    await user.click(screen.getAllByTestId('budget-actions')[0] as HTMLElement);
    await user.click(await screen.findByTestId('edit-budget-action'));
    expect(await screen.findByTestId('edit-budget-dialog')).toBeInTheDocument();
    // Closing runs the table's `if (!next) setEditTarget(null)`.
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByTestId('edit-budget-dialog')).not.toBeInTheDocument());
  });

  it('opens then closes the Archive dialog from a row menu', async () => {
    const user = userEvent.setup();
    renderWithClient(<BudgetsTable />);
    await waitFor(() => expect(screen.getAllByTestId('budget-actions').length).toBe(3));
    await user.click(screen.getAllByTestId('budget-actions')[0] as HTMLElement);
    await user.click(await screen.findByTestId('archive-budget-action'));
    expect(await screen.findByTestId('archive-budget-dialog')).toBeInTheDocument();
    await user.keyboard('{Escape}');
    await waitFor(() =>
      expect(screen.queryByTestId('archive-budget-dialog')).not.toBeInTheDocument(),
    );
  });

  it('renders a fully-spent budget (remaining 0) with a muted remaining figure', async () => {
    server.use(
      http.get('*/budgets', () =>
        HttpResponse.json([
          {
            id: 'b-zero',
            categoryId: 'c1',
            categoryName: 'Exact',
            monthlyLimit: 1000,
            spent: 1000,
            remaining: 0,
            status: 'Warning',
            year: 2026,
            month: 5,
          },
        ]),
      ),
    );
    renderWithClient(<BudgetsTable />);
    const row = await screen.findByTestId('budget-row');
    expect(row.querySelector('.text-muted-foreground')).not.toBeNull();
  });
});
