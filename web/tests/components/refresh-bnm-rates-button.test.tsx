import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { RefreshBnmRatesButton } from '@/src/components/settings/refresh-bnm-rates-button';
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

describe('RefreshBnmRatesButton', () => {
  it('toasts the insert/update/skip counts on a refresh with changes', async () => {
    const user = userEvent.setup();
    renderWithClient(<RefreshBnmRatesButton />);
    // Default handler returns inserted=2, updated=0, skipped=1.
    await user.click(screen.getByTestId('refresh-bnm-rates-button'));
    expect(
      await screen.findByText('Refreshed: 2 added, 0 updated, 1 unchanged'),
    ).toBeInTheDocument();
  });

  it('toasts "up to date" when nothing was inserted or updated', async () => {
    server.use(
      http.post('*/fx-rates/refresh', () =>
        HttpResponse.json({ fetched: 3, inserted: 0, updated: 0, skipped: 3 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<RefreshBnmRatesButton />);
    await user.click(screen.getByTestId('refresh-bnm-rates-button'));
    expect(await screen.findByText('FX rates are up to date.')).toBeInTheDocument();
  });

  it('toasts a friendly error on failure', async () => {
    server.use(http.post('*/fx-rates/refresh', () => HttpResponse.json({}, { status: 500 })));
    const user = userEvent.setup();
    renderWithClient(<RefreshBnmRatesButton />);
    await user.click(screen.getByTestId('refresh-bnm-rates-button'));
    expect(
      await screen.findByText('Failed to refresh from BNM. Try again later.'),
    ).toBeInTheDocument();
  });

  it('shows the spinner while pending', async () => {
    server.use(
      http.post('*/fx-rates/refresh', () => new Promise(() => {}) as unknown as Promise<Response>),
    );
    const user = userEvent.setup();
    renderWithClient(<RefreshBnmRatesButton />);
    await user.click(screen.getByTestId('refresh-bnm-rates-button'));
    expect(await screen.findByTestId('refresh-bnm-spinner')).toBeInTheDocument();
  });
});
