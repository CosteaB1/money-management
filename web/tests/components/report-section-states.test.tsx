import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { BalanceOverTimeSection } from '@/src/components/reports/balance-over-time-section';
import { CategoryBreakdownSection } from '@/src/components/reports/category-breakdown-section';
import { MonthlySummarySection } from '@/src/components/reports/monthly-summary-section';
import { TopPayeesSection } from '@/src/components/reports/top-payees-section';
import { YearOverYearSection } from '@/src/components/reports/year-over-year-section';
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
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('MonthlySummarySection states', () => {
  it('renders the error state', async () => {
    server.use(http.get('*/reports/monthly-summary', () => HttpResponse.json({}, { status: 500 })));
    renderWithClient(<MonthlySummarySection />);
    expect(await screen.findByTestId('monthly-summary-section-error')).toBeInTheDocument();
  });

  it('renders the empty state', async () => {
    server.use(http.get('*/reports/monthly-summary', () => HttpResponse.json([])));
    renderWithClient(<MonthlySummarySection />);
    expect(await screen.findByTestId('monthly-summary-section-empty')).toBeInTheDocument();
  });

  it('color-codes a negative-net month red in the table', async () => {
    server.use(
      http.get('*/reports/monthly-summary', () =>
        HttpResponse.json([
          {
            month: '2026-05',
            income: 1000,
            expense: 4000,
            net: -3000,
            savingsRate: 0,
            transactionCount: 5,
            missingFxRate: false,
          },
        ]),
      ),
    );
    renderWithClient(<MonthlySummarySection />);
    await waitFor(() => {
      expect(document.querySelector('.text-red-600')).not.toBeNull();
    });
  });

  it('shows the missing-FX note when a month lacks a rate', async () => {
    server.use(
      http.get('*/reports/monthly-summary', () =>
        HttpResponse.json([
          {
            month: '2026-05',
            income: 1,
            expense: 1,
            net: 0,
            savingsRate: 0,
            transactionCount: 1,
            missingFxRate: true,
          },
        ]),
      ),
    );
    renderWithClient(<MonthlySummarySection />);
    expect(await screen.findByTestId('monthly-summary-section-missing-fx')).toBeInTheDocument();
  });
});

describe('CategoryBreakdownSection states', () => {
  it('renders the error state', async () => {
    server.use(
      http.get('*/reports/category-breakdown', () => HttpResponse.json({}, { status: 500 })),
    );
    renderWithClient(<CategoryBreakdownSection />);
    expect(await screen.findByTestId('category-breakdown-section-error')).toBeInTheDocument();
  });

  it('renders the empty state', async () => {
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
    expect(await screen.findByTestId('category-breakdown-section-empty')).toBeInTheDocument();
  });

  it('shows the missing-FX note', async () => {
    server.use(
      http.get('*/reports/category-breakdown', () =>
        HttpResponse.json({
          from: '2026-05-01',
          to: '2026-05-23',
          direction: 'Expense',
          totalMdl: 100,
          missingFxRate: true,
          items: [
            {
              categoryId: 'c1',
              categoryName: 'Groceries',
              amountMdl: 100,
              percentage: 1,
              transactionCount: 1,
            },
          ],
        }),
      ),
    );
    renderWithClient(<CategoryBreakdownSection />);
    expect(await screen.findByTestId('category-breakdown-missing-fx')).toBeInTheDocument();
  });
});

describe('BalanceOverTimeSection states', () => {
  // The query is disabled until an account is chosen, so each state test
  // selects one first.
  async function selectAccount() {
    const user = userEvent.setup();
    await user.click(await screen.findByTestId('balance-account'));
    await user.click(await screen.findByRole('option', { name: /XTB/ }));
  }

  it('renders the error state once an account is chosen', async () => {
    server.use(
      http.get('*/reports/balance-over-time', () => HttpResponse.json({}, { status: 500 })),
    );
    renderWithClient(<BalanceOverTimeSection />);
    await selectAccount();
    expect(await screen.findByTestId('balance-over-time-section-error')).toBeInTheDocument();
  });

  it('renders the empty state', async () => {
    server.use(http.get('*/reports/balance-over-time', () => HttpResponse.json([])));
    renderWithClient(<BalanceOverTimeSection />);
    await selectAccount();
    expect(await screen.findByTestId('balance-over-time-section-empty')).toBeInTheDocument();
  });

  it('shows the missing-FX note for a USD account', async () => {
    server.use(
      http.get('*/reports/balance-over-time', () =>
        HttpResponse.json([
          { asOf: '2026-05-01', balance: 1500, balanceMdl: null, missingFxRate: true },
        ]),
      ),
    );
    renderWithClient(<BalanceOverTimeSection />);
    await selectAccount();
    await waitFor(() => {
      expect(screen.getByTestId('balance-over-time-missing-fx')).toBeInTheDocument();
    });
  });
});

describe('TopPayeesSection states', () => {
  it('renders the error state', async () => {
    server.use(http.get('*/reports/top-payees', () => HttpResponse.json({}, { status: 500 })));
    renderWithClient(<TopPayeesSection />);
    expect(await screen.findByTestId('top-payees-section-error')).toBeInTheDocument();
  });

  it('renders the empty state', async () => {
    server.use(http.get('*/reports/top-payees', () => HttpResponse.json([])));
    renderWithClient(<TopPayeesSection />);
    expect(await screen.findByTestId('top-payees-section-empty')).toBeInTheDocument();
  });
});

describe('YearOverYearSection states', () => {
  it('renders the error state', async () => {
    server.use(http.get('*/reports/monthly-summary', () => HttpResponse.json({}, { status: 500 })));
    renderWithClient(<YearOverYearSection />);
    expect(await screen.findByTestId('year-over-year-section-error')).toBeInTheDocument();
  });

  it('shows the missing-FX note', async () => {
    server.use(
      http.get('*/reports/monthly-summary', () =>
        HttpResponse.json([
          {
            month: '2025-04',
            income: 1,
            expense: 1,
            net: 0,
            savingsRate: 0,
            transactionCount: 1,
            missingFxRate: true,
          },
          {
            month: '2026-05',
            income: 1,
            expense: 1,
            net: 0,
            savingsRate: 0,
            transactionCount: 1,
            missingFxRate: false,
          },
        ]),
      ),
    );
    renderWithClient(<YearOverYearSection />);
    expect(await screen.findByTestId('year-over-year-missing-fx')).toBeInTheDocument();
  });

  it('renders the empty state when no months come back', async () => {
    server.use(http.get('*/reports/monthly-summary', () => HttpResponse.json([])));
    renderWithClient(<YearOverYearSection />);
    expect(await screen.findByTestId('year-over-year-section-empty')).toBeInTheDocument();
  });

  it('color-codes a negative year-over-year delta (current net below prior)', async () => {
    // 24 months: year 2024 (prior) all net 8000, year 2025 (current) all net 1000.
    // Every paired month then has current < prior → negative delta → red.
    const months: Array<Record<string, unknown>> = [];
    for (const [year, net] of [
      [2024, 8000],
      [2025, 1000],
    ] as const) {
      for (let m = 1; m <= 12; m++) {
        months.push({
          month: `${year}-${String(m).padStart(2, '0')}`,
          income: net + 2000,
          expense: 2000,
          net,
          savingsRate: 0.5,
          transactionCount: 5,
          missingFxRate: false,
        });
      }
    }
    server.use(http.get('*/reports/monthly-summary', () => HttpResponse.json(months)));
    renderWithClient(<YearOverYearSection />);
    const rows = await screen.findAllByTestId('year-over-year-row');
    expect(rows.length).toBeGreaterThan(0);
    // At least one delta cell is red (current 1000 < prior 8000).
    expect(rows.some((r) => r.querySelector('.text-red-600') !== null)).toBe(true);
  });
});
