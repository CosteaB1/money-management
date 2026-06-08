import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { RebuildPeriodsButton } from '@/src/components/budgets/rebuild-periods-button';
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

describe('RebuildPeriodsButton', () => {
  it('renders the rebuild action', () => {
    renderWithClient(<RebuildPeriodsButton />);

    const button = screen.getByTestId('rebuild-budgets-button');
    expect(button).toBeInTheDocument();
    expect(button).toHaveTextContent(/rebuild periods/i);
  });

  it('posts to rebuild-all-periods and shows a success toast with counts', async () => {
    let rebuildCallCount = 0;
    server.use(
      http.post('*/budgets/rebuild-all-periods', () => {
        rebuildCallCount += 1;
        return HttpResponse.json({ budgetsRebuilt: 2, periodsAffected: 4 });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<RebuildPeriodsButton />);

    await user.click(screen.getByTestId('rebuild-budgets-button'));

    await waitFor(() => {
      expect(rebuildCallCount).toBe(1);
    });

    // Toast renders inside the Toaster portal — match by visible text.
    await waitFor(() => {
      expect(screen.getByText('Rebuilt 2 budgets · 4 periods updated')).toBeInTheDocument();
    });
  });

  it('shows the spinner while the rebuild is pending', async () => {
    server.use(
      http.post(
        '*/budgets/rebuild-all-periods',
        () => new Promise(() => {}) as unknown as Promise<Response>,
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<RebuildPeriodsButton />);
    await user.click(screen.getByTestId('rebuild-budgets-button'));
    expect(await screen.findByTestId('rebuild-budgets-spinner')).toBeInTheDocument();
    expect(screen.getByTestId('rebuild-budgets-button')).toHaveTextContent(/rebuilding/i);
  });

  it('surfaces the backend ProblemDetails detail in an error toast', async () => {
    server.use(
      http.post('*/budgets/rebuild-all-periods', () =>
        HttpResponse.json(
          {
            type: 'budget.rebuild_failed',
            title: 'Bad Request',
            status: 400,
            detail: 'Could not rebuild budget periods.',
          },
          { status: 400 },
        ),
      ),
    );

    const user = userEvent.setup();
    renderWithClient(<RebuildPeriodsButton />);

    await user.click(screen.getByTestId('rebuild-budgets-button'));

    await waitFor(() => {
      expect(screen.getByText('Could not rebuild budget periods.')).toBeInTheDocument();
    });
  });
});
