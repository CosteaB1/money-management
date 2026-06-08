import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { FxRatesTable } from '@/src/components/settings/fx-rates-table';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';

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

describe('FxRatesTable', () => {
  it('renders the seeded rows with Manual and BNM source badges', async () => {
    renderWithClient(<FxRatesTable />);
    await waitFor(() => expect(screen.getAllByTestId('fx-rate-row').length).toBe(2));
    const badges = screen.getAllByTestId('fx-rate-source-badge');
    const sources = badges.map((b) => b.getAttribute('data-source'));
    expect(sources).toContain('Manual');
    expect(sources).toContain('BnmAuto');
  });

  it('deletes a rate and toasts success', async () => {
    const user = userEvent.setup();
    renderWithClient(<FxRatesTable />);
    await waitFor(() => expect(screen.getAllByTestId('delete-fx-rate').length).toBe(2));
    await user.click(screen.getAllByTestId('delete-fx-rate')[0] as HTMLElement);
    await waitFor(() => {
      expect(screen.getByText(/Removed USD→MDL/)).toBeInTheDocument();
    });
  });

  it('surfaces a delete failure as an error toast', async () => {
    server.use(
      http.delete('*/fx-rates/:id', () => HttpResponse.json({ detail: 'busy' }, { status: 500 })),
    );
    const user = userEvent.setup();
    renderWithClient(<FxRatesTable />);
    await waitFor(() => expect(screen.getAllByTestId('delete-fx-rate').length).toBe(2));
    await user.click(screen.getAllByTestId('delete-fx-rate')[0] as HTMLElement);
    expect(await screen.findByText('busy')).toBeInTheDocument();
  });

  it('renders the error state', async () => {
    server.use(http.get('*/fx-rates', () => HttpResponse.json({}, { status: 500 })));
    renderWithClient(<FxRatesTable />);
    expect(await screen.findByText('Failed to load FX rates.')).toBeInTheDocument();
  });

  it('renders the empty state', async () => {
    server.use(
      http.get('*/fx-rates', () =>
        HttpResponse.json({ items: [], totalCount: 0, pageNumber: 1, pageSize: 25, totalPages: 1 }),
      ),
    );
    renderWithClient(<FxRatesTable />);
    expect(await screen.findByText(/No FX rates yet/)).toBeInTheDocument();
  });

  it('paginates when there is more than one page', async () => {
    const makeRate = (i: number) => ({
      id: `fx-${i}`,
      fromCurrency: 'USD',
      toCurrency: 'MDL',
      rate: 17.5,
      asOf: '2026-05-01',
      createdAt: '2026-05-01T00:00:00Z',
      updatedAt: '2026-05-01T00:00:00Z',
      source: 'Manual' as const,
    });
    server.use(
      http.get('*/fx-rates', ({ request }) => {
        const page = Number(new URL(request.url).searchParams.get('page') ?? '1');
        return HttpResponse.json({
          items: [makeRate(page)],
          totalCount: 2,
          pageNumber: page,
          pageSize: 1,
          totalPages: 2,
        });
      }),
    );
    const user = userEvent.setup();
    renderWithClient(<FxRatesTable />);
    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Next page' }));
    expect(await screen.findByText('Page 2 of 2')).toBeInTheDocument();
    // Previous wraps back to page 1.
    await user.click(screen.getByRole('button', { name: 'Previous page' }));
    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument();
  });
});
