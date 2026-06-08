import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { BackfillBnmRatesButton } from '@/src/components/settings/backfill-bnm-rates-button';
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

describe('Backfill BNM rates button', () => {
  it('opens the dialog, prefills From from the earliest account opening date', async () => {
    const user = userEvent.setup();
    renderWithClient(<BackfillBnmRatesButton />);

    await user.click(screen.getByTestId('backfill-bnm-rates-button'));

    await waitFor(() => {
      expect(screen.getByTestId('backfill-bnm-dialog')).toBeInTheDocument();
    });

    // The seeded archived account "Old Revolut" opens 2023-02-01, the earliest
    // across all accounts (incl. archived) — the From input defaults to it.
    await waitFor(() => {
      expect(screen.getByTestId('backfill-from-input')).toHaveValue('2023-02-01');
    });
  });

  it('submits the backfill and shows a success toast with day + row counts', async () => {
    let backfillCallCount = 0;
    server.use(
      http.post('*/fx-rates/backfill', async () => {
        backfillCallCount += 1;
        return HttpResponse.json({
          daysProcessed: 30,
          fetched: 90,
          inserted: 12,
          updated: 3,
          skipped: 75,
        });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<BackfillBnmRatesButton />);

    await user.click(screen.getByTestId('backfill-bnm-rates-button'));

    await waitFor(() => {
      expect(screen.getByTestId('backfill-submit-button')).toBeInTheDocument();
    });

    await user.click(screen.getByTestId('backfill-submit-button'));

    await waitFor(() => {
      expect(backfillCallCount).toBe(1);
    });

    // Toast renders inside the Toaster portal — match by visible text.
    await waitFor(() => {
      expect(screen.getByText('Backfilled 30 days · 12 added, 3 updated')).toBeInTheDocument();
    });
  });

  it('surfaces the 400 ProblemDetails detail verbatim in an error toast', async () => {
    server.use(
      http.post('*/fx-rates/backfill', async () =>
        HttpResponse.json(
          {
            type: 'fx_rate.invalid_range',
            title: 'Bad Request',
            status: 400,
            detail: 'Backfill range cannot exceed 2 years.',
          },
          { status: 400 },
        ),
      ),
    );

    const user = userEvent.setup();
    renderWithClient(<BackfillBnmRatesButton />);

    await user.click(screen.getByTestId('backfill-bnm-rates-button'));

    await waitFor(() => {
      expect(screen.getByTestId('backfill-submit-button')).toBeInTheDocument();
    });

    await user.click(screen.getByTestId('backfill-submit-button'));

    await waitFor(() => {
      expect(screen.getByText('Backfill range cannot exceed 2 years.')).toBeInTheDocument();
    });
  });

  it('falls back to ~12 months ago for From when there are no accounts', async () => {
    server.use(http.get('*/accounts', () => HttpResponse.json([])));
    const user = userEvent.setup();
    renderWithClient(<BackfillBnmRatesButton />);
    await user.click(screen.getByTestId('backfill-bnm-rates-button'));
    await waitFor(() => {
      // Not the seeded 2023-02-01 — a computed yyyy-MM-dd ~12 months back.
      const value = (screen.getByTestId('backfill-from-input') as HTMLInputElement).value;
      expect(value).toMatch(/^\d{4}-\d{2}-\d{2}$/);
      expect(value).not.toBe('2023-02-01');
    });
  });

  it('includes the To date in the request when the user picks one', async () => {
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/fx-rates/backfill', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({
          daysProcessed: 5,
          fetched: 10,
          inserted: 2,
          updated: 1,
          skipped: 7,
        });
      }),
    );
    const user = userEvent.setup();
    renderWithClient(<BackfillBnmRatesButton />);
    await user.click(screen.getByTestId('backfill-bnm-rates-button'));
    await screen.findByTestId('backfill-to-input');
    await user.type(screen.getByTestId('backfill-to-input'), '2026-05-31');
    await user.click(screen.getByTestId('backfill-submit-button'));
    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ to: '2026-05-31' });
  });

  it('shows the spinner while the backfill is pending', async () => {
    server.use(
      http.post('*/fx-rates/backfill', () => new Promise(() => {}) as unknown as Promise<Response>),
    );
    const user = userEvent.setup();
    renderWithClient(<BackfillBnmRatesButton />);
    await user.click(screen.getByTestId('backfill-bnm-rates-button'));
    await user.click(await screen.findByTestId('backfill-submit-button'));
    expect(await screen.findByTestId('backfill-spinner')).toBeInTheDocument();
  });
});
