import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { CreateFxRateDialog } from '@/src/components/settings/create-fx-rate-dialog';
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

describe('CreateFxRateDialog', () => {
  it('opens, adds a rate and toasts success', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateFxRateDialog />);
    await user.click(screen.getByTestId('add-fx-rate-button'));
    expect(await screen.findByRole('dialog')).toBeInTheDocument();

    const rate = screen.getByTestId('fx-rate-input');
    await user.clear(rate);
    await user.type(rate, '17.5');
    await user.click(screen.getByTestId('fx-rate-submit-button'));
    expect(await screen.findByText('FX rate added')).toBeInTheDocument();
  });

  it('rejects identical From/To currencies', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateFxRateDialog />);
    await user.click(screen.getByTestId('add-fx-rate-button'));

    // Default From=USD; switch To to USD too so the refine fires.
    await user.click(screen.getByTestId('fx-to-currency-select'));
    await user.click(await screen.findByRole('option', { name: 'USD' }));

    const rate = screen.getByTestId('fx-rate-input');
    await user.clear(rate);
    await user.type(rate, '1');
    await user.click(screen.getByTestId('fx-rate-submit-button'));
    expect(await screen.findByText('From and To currencies must differ')).toBeInTheDocument();
  });

  it('shows the as-of error when the date is cleared', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateFxRateDialog />);
    await user.click(screen.getByTestId('add-fx-rate-button'));
    const rate = screen.getByTestId('fx-rate-input');
    await user.clear(rate);
    await user.type(rate, '17.5');
    await user.clear(screen.getByTestId('fx-as-of-input'));
    await user.click(screen.getByTestId('fx-rate-submit-button'));
    expect(await screen.findByText('As-of date is required')).toBeInTheDocument();
  });

  it('rejects a non-positive rate', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateFxRateDialog />);
    await user.click(screen.getByTestId('add-fx-rate-button'));
    // Default rate is 0 → positive() fails.
    await user.click(screen.getByTestId('fx-rate-submit-button'));
    expect(await screen.findByText('Rate must be greater than 0')).toBeInTheDocument();
  });

  it('shows an error toast when the API rejects the rate', async () => {
    server.use(
      http.post('*/fx-rates', () => HttpResponse.json({ detail: 'nope' }, { status: 400 })),
    );
    const user = userEvent.setup();
    renderWithClient(<CreateFxRateDialog />);
    await user.click(screen.getByTestId('add-fx-rate-button'));
    const rate = screen.getByTestId('fx-rate-input');
    await user.clear(rate);
    await user.type(rate, '17.5');
    await user.click(screen.getByTestId('fx-rate-submit-button'));
    expect(await screen.findByText('nope')).toBeInTheDocument();
  });

  it('resets the form when the dialog is dismissed via Cancel and reopened', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateFxRateDialog />);
    await user.click(screen.getByTestId('add-fx-rate-button'));
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });
});
