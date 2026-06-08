import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { TopPayeesSection } from '@/src/components/reports/top-payees-section';
import { server } from '@/src/lib/mocks/server';

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('TopPayeesSection', () => {
  it('renders the seeded rows in descending order by amount', async () => {
    renderWithClient(<TopPayeesSection />);
    await waitFor(() => {
      expect(screen.getByTestId('top-payees-table')).toBeInTheDocument();
    });
    const rows = screen.getAllByTestId('top-payees-row');
    expect(rows.length).toBe(3);

    // The 3rd cell of each row carries the formatted amount. Strip the
    // grouping separators and currency suffix to compare numerically —
    // `Intl.NumberFormat('ro-MD')` formats 1800 as "1.800,00 L".
    const amountCellValue = (row: Element) => {
      const cells = row.querySelectorAll('td');
      const text = cells[2]?.textContent ?? '';
      const digits = text.replace(/[^\d,]/g, '').replace(',', '.');
      return Number.parseFloat(digits);
    };
    const amounts = rows.map((r) => amountCellValue(r));
    for (let i = 1; i < amounts.length; i++) {
      expect(amounts[i - 1]).toBeGreaterThanOrEqual(amounts[i] ?? 0);
    }
    expect(rows[0]?.textContent ?? '').toMatch(/Linella/);
    expect(rows[0]?.textContent ?? '').toMatch(/LINELLA SRL/);
  });

  it('refetches with a new limit when the limit input changes', async () => {
    let lastLimit = '';
    server.use(
      http.get('*/reports/top-payees', ({ request }) => {
        const url = new URL(request.url);
        lastLimit = url.searchParams.get('limit') ?? '';
        return HttpResponse.json([
          {
            payee: 'A',
            originalDescription: 'A INC',
            amountMdl: 100,
            transactionCount: 1,
          },
        ]);
      }),
    );
    renderWithClient(<TopPayeesSection />);
    await waitFor(() => {
      expect(lastLimit).toBe('10');
    });

    const limitInput = screen.getByTestId('payees-limit') as HTMLInputElement;
    await act(async () => {
      fireEvent.change(limitInput, { target: { value: '5' } });
    });

    await waitFor(() => {
      expect(lastLimit).toBe('5');
    });
  });

  it('shows the empty hint when the endpoint returns []', async () => {
    server.use(http.get('*/reports/top-payees', () => HttpResponse.json([])));
    renderWithClient(<TopPayeesSection />);
    await waitFor(() => {
      expect(screen.getByTestId('top-payees-section-empty')).toBeInTheDocument();
    });
  });

  it('renders an error state on 500', async () => {
    server.use(
      http.get('*/reports/top-payees', () => HttpResponse.json({ error: 'boom' }, { status: 500 })),
    );
    renderWithClient(<TopPayeesSection />);
    await waitFor(() => {
      expect(screen.getByTestId('top-payees-section-error')).toBeInTheDocument();
    });
  });
});
