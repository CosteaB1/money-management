import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { CategoryBreakdownSection } from '@/src/components/reports/category-breakdown-section';
import { server } from '@/src/lib/mocks/server';

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
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('CategoryBreakdownSection', () => {
  it('renders the seeded categories, percentages and the Uncategorized bucket', async () => {
    renderWithClient(<CategoryBreakdownSection />);
    await waitFor(() => {
      expect(screen.getByTestId('category-breakdown-chart')).toBeInTheDocument();
    });
    const rows = screen.getAllByTestId('category-breakdown-row');
    expect(rows.length).toBe(3);

    // sr-only enumeration of points
    expect(screen.getAllByTestId('category-breakdown-point').length).toBe(3);

    // The Uncategorized bucket should be flagged in the row markup so
    // styling/tests can target it independently of the visible label.
    const uncategorizedRow = rows.find((r) => r.getAttribute('data-uncategorized') === 'true');
    expect(uncategorizedRow).toBeDefined();
    expect(uncategorizedRow?.textContent ?? '').toMatch(/Uncategorized/);

    // Top row (Groceries) carries its percentage — ~44%.
    const top = rows[0];
    expect(top?.textContent ?? '').toMatch(/Groceries/);
    expect(top?.textContent ?? '').toMatch(/44/);

    expect(screen.getByTestId('category-breakdown-total').textContent).toMatch(/4\D?500/);
  });

  it('changes the query key when the direction toggle flips to Income', async () => {
    let lastDirection = '';
    server.use(
      http.get('*/reports/category-breakdown', ({ request }) => {
        const url = new URL(request.url);
        lastDirection = url.searchParams.get('direction') ?? '';
        return HttpResponse.json({
          from: '2026-05-01',
          to: '2026-05-23',
          direction: lastDirection,
          totalMdl: 1000,
          missingFxRate: false,
          items: [
            {
              categoryId: 'cat-1',
              categoryName: 'Salary',
              amountMdl: 1000,
              percentage: 1,
              transactionCount: 1,
            },
          ],
        });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<CategoryBreakdownSection />);
    await waitFor(() => {
      expect(lastDirection).toBe('Expense');
    });

    await user.click(screen.getByTestId('category-direction-income'));

    await waitFor(() => {
      expect(lastDirection).toBe('Income');
    });
  });

  it('shows the empty hint when the endpoint returns an empty items list', async () => {
    server.use(
      http.get('*/reports/category-breakdown', () =>
        HttpResponse.json({
          from: '2026-05-01',
          to: '2026-05-23',
          direction: 'Expense',
          totalMdl: 0,
          missingFxRate: false,
          items: [],
        }),
      ),
    );
    renderWithClient(<CategoryBreakdownSection />);
    await waitFor(() => {
      expect(screen.getByTestId('category-breakdown-section-empty')).toBeInTheDocument();
    });
  });

  it('surfaces the missing-FX warning when the response flag is true', async () => {
    server.use(
      http.get('*/reports/category-breakdown', () =>
        HttpResponse.json({
          from: '2026-05-01',
          to: '2026-05-23',
          direction: 'Expense',
          totalMdl: 1000,
          missingFxRate: true,
          items: [
            {
              categoryId: 'cat-1',
              categoryName: 'Groceries',
              amountMdl: 1000,
              percentage: 1,
              transactionCount: 3,
            },
          ],
        }),
      ),
    );
    renderWithClient(<CategoryBreakdownSection />);
    await waitFor(() => {
      expect(screen.getByTestId('category-breakdown-missing-fx')).toBeInTheDocument();
    });
  });
});
