import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { UpdateBalanceDialog } from '@/src/components/accounts/update-balance-dialog';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import type { AccountDto } from '@/src/types/api';

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

function renderWithToaster(ui: React.ReactElement) {
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

const xtbAccount: AccountDto = {
  id: '44444444-4444-4444-4444-444444444444',
  name: 'XTB',
  type: 'Brokerage',
  currency: 'USD',
  openingDate: '2024-09-10',
  isArchived: false,
  notes: null,
  // Live computed balance (anchor 1500 USD + 250 USD adjustment income).
  balance: 1750,
  balanceMdl: 30625,
};

describe('UpdateBalanceDialog', () => {
  it('defaults to Investment mode with an Amount field', async () => {
    renderWithClient(
      <UpdateBalanceDialog account={xtbAccount} open={true} onOpenChange={() => {}} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('update-balance-dialog')).toBeInTheDocument();
    });
    // Investment is the default → Amount field + label, no new-balance field.
    expect(screen.getByLabelText(/amount \(USD\)/i)).toBeInTheDocument();
    expect(screen.getByTestId('balance-amount-input')).toBeInTheDocument();
    expect(screen.queryByTestId('adjust-new-balance-input')).not.toBeInTheDocument();
    // The current-balance preview only renders in Adjustment mode.
    expect(screen.queryByTestId('update-balance-current')).not.toBeInTheDocument();
  });

  it('swaps to the New balance field + current-balance preview in Adjustment mode', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <UpdateBalanceDialog account={xtbAccount} open={true} onOpenChange={() => {}} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('balance-kind-adjustment')).toBeInTheDocument();
    });
    await user.click(screen.getByTestId('balance-kind-adjustment'));

    expect(screen.getByLabelText(/new balance \(USD\)/i)).toBeInTheDocument();
    expect(screen.getByTestId('adjust-new-balance-input')).toBeInTheDocument();
    // account.balance is 1750 USD → "1.750,00 USD" in the ro-MD locale.
    await waitFor(() => {
      expect(screen.getByTestId('update-balance-current')).toHaveTextContent(/1[.\s]?750/);
      expect(screen.getByTestId('update-balance-current')).toHaveTextContent(/USD/);
    });
  });

  it('rejects a non-positive amount in Investment mode', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <UpdateBalanceDialog account={xtbAccount} open={true} onOpenChange={() => {}} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('adjust-submit-button')).toBeInTheDocument();
    });

    const input = screen.getByTestId('balance-amount-input') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '0');
    await user.click(screen.getByTestId('adjust-submit-button'));

    await waitFor(() => {
      expect(screen.getByText(/amount must be greater than 0/i)).toBeInTheDocument();
    });
  });

  it('posts kind=Investment with value and toasts the investment message', async () => {
    let captured: Record<string, unknown> | null = null;
    let capturedUrl = '';
    server.use(
      http.post('*/accounts/:id/balance-changes', async ({ request }) => {
        capturedUrl = request.url;
        captured = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ transactionId: 'mock-tx', delta: 500 }, { status: 201 });
      }),
    );

    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    renderWithClient(
      <UpdateBalanceDialog account={xtbAccount} open={true} onOpenChange={onOpenChange} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('balance-amount-input')).toBeInTheDocument();
    });

    const input = screen.getByTestId('balance-amount-input') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '500');

    const notes = screen.getByTestId('adjust-notes-input');
    await user.type(notes, 'Wired more capital');

    await user.click(screen.getByTestId('adjust-submit-button'));

    await waitFor(() => {
      expect(captured).not.toBeNull();
    });
    expect(capturedUrl).toContain('/accounts/44444444-4444-4444-4444-444444444444/balance-changes');
    expect(captured).toMatchObject({
      kind: 'Investment',
      value: 500,
      notes: 'Wired more capital',
    });
    // Dialog self-closes on success.
    await waitFor(() => {
      expect(onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it('posts kind=Adjustment with value = the new balance', async () => {
    let captured: Record<string, unknown> | null = null;
    server.use(
      http.post('*/accounts/:id/balance-changes', async ({ request }) => {
        captured = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ transactionId: 'mock-tx', delta: 250 }, { status: 201 });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(
      <UpdateBalanceDialog account={xtbAccount} open={true} onOpenChange={() => {}} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('balance-kind-adjustment')).toBeInTheDocument();
    });
    await user.click(screen.getByTestId('balance-kind-adjustment'));

    const input = screen.getByTestId('adjust-new-balance-input') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '2000');

    await user.click(screen.getByTestId('adjust-submit-button'));

    await waitFor(() => {
      expect(captured).not.toBeNull();
    });
    expect(captured).toMatchObject({ kind: 'Adjustment', value: 2000 });
  });

  it('surfaces a 0-delta backend error on the new-balance field in Adjustment mode', async () => {
    server.use(
      http.post('*/accounts/:id/balance-changes', async () =>
        HttpResponse.json({ error: 'No change in balance' }, { status: 409 }),
      ),
    );

    const user = userEvent.setup();
    renderWithClient(
      <UpdateBalanceDialog account={xtbAccount} open={true} onOpenChange={() => {}} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('balance-kind-adjustment')).toBeInTheDocument();
    });
    await user.click(screen.getByTestId('balance-kind-adjustment'));

    const input = screen.getByTestId('adjust-new-balance-input') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '1750');

    await user.click(screen.getByTestId('adjust-submit-button'));

    await waitFor(() => {
      expect(screen.getByText(/no change in balance/i)).toBeInTheDocument();
    });
  });

  it('records a withdrawal and toasts the withdrawal message', async () => {
    const user = userEvent.setup();
    renderWithToaster(
      <UpdateBalanceDialog account={xtbAccount} open={true} onOpenChange={() => {}} />,
    );
    await waitFor(() => expect(screen.getByTestId('balance-kind-withdrawal')).toBeInTheDocument());
    await user.click(screen.getByTestId('balance-kind-withdrawal'));
    const input = screen.getByTestId('balance-amount-input');
    await user.clear(input);
    await user.type(input, '300');
    await user.click(screen.getByTestId('adjust-submit-button'));
    expect(await screen.findByText(/Recorded withdrawal of/)).toBeInTheDocument();
  });

  it('shows a generic error toast when an Investment fails', async () => {
    server.use(
      http.post('*/accounts/:id/balance-changes', () =>
        HttpResponse.json({ detail: 'Server error' }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderWithToaster(
      <UpdateBalanceDialog account={xtbAccount} open={true} onOpenChange={() => {}} />,
    );
    await waitFor(() => expect(screen.getByTestId('balance-amount-input')).toBeInTheDocument());
    const input = screen.getByTestId('balance-amount-input');
    await user.clear(input);
    await user.type(input, '500');
    await user.click(screen.getByTestId('adjust-submit-button'));
    expect(await screen.findByText('Server error')).toBeInTheDocument();
  });

  it('shows the date error when the date is cleared', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <UpdateBalanceDialog account={xtbAccount} open={true} onOpenChange={() => {}} />,
    );
    await waitFor(() => expect(screen.getByTestId('balance-amount-input')).toBeInTheDocument());

    await user.clear(screen.getByTestId('balance-amount-input'));
    await user.type(screen.getByTestId('balance-amount-input'), '500');
    await user.clear(screen.getByTestId('adjust-date-input'));
    await user.click(screen.getByTestId('adjust-submit-button'));

    await waitFor(() => {
      expect(screen.getByTestId('adjust-date-input')).toHaveAttribute('aria-invalid', 'true');
    });
  });

  it('shows the notes error when the note exceeds the max length', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <UpdateBalanceDialog account={xtbAccount} open={true} onOpenChange={() => {}} />,
    );
    await waitFor(() => expect(screen.getByTestId('balance-amount-input')).toBeInTheDocument());

    await user.clear(screen.getByTestId('balance-amount-input'));
    await user.type(screen.getByTestId('balance-amount-input'), '500');
    // The textarea's maxLength caps typed input; set an over-limit value
    // directly so the Zod `.max(500)` refinement renders its error.
    fireEvent.change(screen.getByTestId('adjust-notes-input'), {
      target: { value: 'x'.repeat(501) },
    });
    await user.click(screen.getByTestId('adjust-submit-button'));

    await waitFor(() => {
      expect(screen.getByTestId('adjust-notes-input')).toHaveAttribute('aria-invalid', 'true');
    });
  });
});
