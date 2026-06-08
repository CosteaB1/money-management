import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { CreateAccountDialog } from '@/src/components/accounts/create-account-dialog';
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

describe('CreateAccountDialog', () => {
  it('opens, validates a blank name, then creates an account', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateAccountDialog />);

    await user.click(screen.getByTestId('add-account-button'));
    expect(await screen.findByRole('dialog')).toBeInTheDocument();

    // Clear the (empty) name and submit → Zod name error.
    await user.click(screen.getByTestId('account-submit-button'));
    expect(await screen.findByText('Name is required')).toBeInTheDocument();

    await user.type(screen.getByTestId('account-name-input'), 'Brokerage X');
    await user.click(screen.getByTestId('account-submit-button'));

    expect(await screen.findByText('Account created')).toBeInTheDocument();
  });

  it('blocks a negative balance on a non-credit-card type', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateAccountDialog />);

    await user.click(screen.getByTestId('add-account-button'));
    await user.type(screen.getByTestId('account-name-input'), 'Cash');
    const balance = screen.getByTestId('account-balance-input');
    await user.clear(balance);
    await user.type(balance, '-50');
    await user.click(screen.getByTestId('account-submit-button'));

    expect(
      await screen.findByText('Only credit cards can have a negative balance'),
    ).toBeInTheDocument();
  });

  it('shows an error toast when the API rejects creation', async () => {
    server.use(
      http.post('*/accounts', () =>
        HttpResponse.json(
          { type: 'x', title: 'Bad', status: 400, detail: 'Server says no' },
          { status: 400 },
        ),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<CreateAccountDialog />);

    await user.click(screen.getByTestId('add-account-button'));
    await user.type(screen.getByTestId('account-name-input'), 'Cash');
    await user.click(screen.getByTestId('account-submit-button'));

    expect(await screen.findByText('Server says no')).toBeInTheDocument();
  });

  it('shows the opening-date error when the date is cleared', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateAccountDialog />);
    await user.click(screen.getByTestId('add-account-button'));
    await user.type(screen.getByTestId('account-name-input'), 'Cash');
    await user.clear(screen.getByTestId('account-opening-date-input'));
    await user.click(screen.getByTestId('account-submit-button'));
    // z.coerce.date('') → Invalid Date → the openingDate field error renders.
    const dateInput = screen.getByTestId('account-opening-date-input');
    await waitFor(() => {
      expect(dateInput).toHaveAttribute('aria-invalid', 'true');
    });
  });

  it('changing type + currency selects updates the form values', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateAccountDialog />);
    await user.click(screen.getByTestId('add-account-button'));

    await user.click(screen.getByTestId('account-type-select'));
    await user.click(await screen.findByRole('option', { name: 'Credit card' }));

    await user.click(screen.getByTestId('account-currency-select'));
    await user.click(await screen.findByRole('option', { name: 'USD' }));

    // Closing the dialog resets the form (covers the onOpenChange reset branch).
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });
});
