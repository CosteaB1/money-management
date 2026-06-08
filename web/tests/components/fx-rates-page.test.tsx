import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { CreateFxRateDialog } from '@/src/components/settings/create-fx-rate-dialog';
import { FxRatesTable } from '@/src/components/settings/fx-rates-table';
import { RefreshBnmRatesButton } from '@/src/components/settings/refresh-bnm-rates-button';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';

function renderWithClient(ui: React.ReactElement) {
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

describe('FX rates settings page', () => {
  it('renders the rates table with mocked rows', async () => {
    renderWithClient(<FxRatesTable />);

    await waitFor(() => {
      const rows = screen.getAllByTestId('fx-rate-row');
      expect(rows.length).toBeGreaterThan(0);
    });

    expect(screen.getByText('USD')).toBeInTheDocument();
    expect(screen.getByText('EUR')).toBeInTheDocument();
  });

  it('renders Source badges distinguishing Manual from BnmAuto rows', async () => {
    renderWithClient(<FxRatesTable />);

    await waitFor(() => {
      expect(screen.getAllByTestId('fx-rate-row').length).toBeGreaterThan(0);
    });

    const badges = screen.getAllByTestId('fx-rate-source-badge');
    // Handlers seed one Manual row (USD→MDL) and one BnmAuto row (EUR→MDL).
    const sources = badges.map((b) => b.getAttribute('data-source'));
    expect(sources).toContain('Manual');
    expect(sources).toContain('BnmAuto');

    expect(screen.getByText('Manual')).toBeInTheDocument();
    expect(screen.getByText('BNM')).toBeInTheDocument();
  });

  it('attaches a refresh-note title to the delete button for BNM rows only', async () => {
    renderWithClient(<FxRatesTable />);

    await waitFor(() => {
      expect(screen.getAllByTestId('fx-rate-row').length).toBeGreaterThan(0);
    });

    const rows = screen.getAllByTestId('fx-rate-row');
    // The badge inside each row tells us which source the row carries.
    let manualDeleteTitle: string | null = null;
    let bnmDeleteTitle: string | null = null;
    for (const row of rows) {
      const badge = row.querySelector('[data-testid="fx-rate-source-badge"]');
      const del = row.querySelector('[data-testid="delete-fx-rate"]');
      if (badge?.getAttribute('data-source') === 'Manual') {
        manualDeleteTitle = del?.getAttribute('title') ?? null;
      } else if (badge?.getAttribute('data-source') === 'BnmAuto') {
        bnmDeleteTitle = del?.getAttribute('title') ?? null;
      }
    }
    expect(bnmDeleteTitle).toMatch(/BNM rates will be re-fetched/i);
    // Manual rows should not have a title at all.
    expect(manualDeleteTitle).toBeNull();
  });

  it('opens the add-rate dialog and shows validation errors when rate is invalid', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateFxRateDialog />);

    await user.click(screen.getByTestId('add-fx-rate-button'));

    await waitFor(() => {
      expect(screen.getByTestId('fx-rate-submit-button')).toBeInTheDocument();
    });

    // Rate defaults to 0, which is not positive — validation should fire.
    await user.click(screen.getByTestId('fx-rate-submit-button'));

    await waitFor(() => {
      expect(screen.getByText('Rate must be greater than 0')).toBeInTheDocument();
    });
  });

  it('dispatches the refresh mutation and shows a success toast with the result counts', async () => {
    let refreshCallCount = 0;
    server.use(
      http.post('*/fx-rates/refresh', async () => {
        refreshCallCount += 1;
        return HttpResponse.json({ fetched: 4, inserted: 2, updated: 1, skipped: 1 });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<RefreshBnmRatesButton />);

    await user.click(screen.getByTestId('refresh-bnm-rates-button'));

    await waitFor(() => {
      expect(refreshCallCount).toBe(1);
    });

    // Toast renders inside the Toaster portal — match by visible text.
    await waitFor(() => {
      expect(screen.getByText('Refreshed: 2 added, 1 updated, 1 unchanged')).toBeInTheDocument();
    });
  });

  it('shows the "up to date" toast when no rows were inserted or updated', async () => {
    server.use(
      http.post('*/fx-rates/refresh', async () =>
        HttpResponse.json({ fetched: 3, inserted: 0, updated: 0, skipped: 3 }),
      ),
    );

    const user = userEvent.setup();
    renderWithClient(<RefreshBnmRatesButton />);

    await user.click(screen.getByTestId('refresh-bnm-rates-button'));

    await waitFor(() => {
      expect(screen.getByText('FX rates are up to date.')).toBeInTheDocument();
    });
  });

  it('surfaces an error toast when the refresh endpoint fails', async () => {
    server.use(
      http.post('*/fx-rates/refresh', async () =>
        HttpResponse.json({ error: 'BNM unreachable' }, { status: 500 }),
      ),
    );

    const user = userEvent.setup();
    renderWithClient(<RefreshBnmRatesButton />);

    await user.click(screen.getByTestId('refresh-bnm-rates-button'));

    await waitFor(() => {
      expect(screen.getByText('Failed to refresh from BNM. Try again later.')).toBeInTheDocument();
    });
  });
});
