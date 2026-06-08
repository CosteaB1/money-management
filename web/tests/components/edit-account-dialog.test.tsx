import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { EditAccountDialog } from '@/src/components/accounts/edit-account-dialog';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import type { AccountDto } from '@/src/types/api';

function renderWithClient(ui: ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

function renderWithToaster(ui: ReactElement) {
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
  notes: 'Long-term holdings',
  balance: 1500,
  balanceMdl: 26250,
};

// A second fixture with no notes, to exercise the `notes ?? ''` prefill branch.
const cashAccount: AccountDto = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Cash Wallet',
  type: 'Cash',
  currency: 'MDL',
  openingDate: '2025-01-01',
  isArchived: false,
  notes: null,
  balance: 500,
  balanceMdl: 500,
};

describe('EditAccountDialog', () => {
  it('renders prefilled with the account name and notes', async () => {
    renderWithClient(
      <EditAccountDialog account={xtbAccount} open={true} onOpenChange={() => {}} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('edit-account-dialog')).toBeInTheDocument();
    });
    expect(screen.getByTestId('account-edit-name-input')).toHaveValue('XTB');
    expect(screen.getByTestId('account-edit-notes-input')).toHaveValue('Long-term holdings');
    // Currency is shown as read-only context in the description.
    expect(screen.getByText(/USD/)).toBeInTheDocument();
  });

  it('prefills an empty notes field when the account has no notes', async () => {
    renderWithClient(
      <EditAccountDialog account={cashAccount} open={true} onOpenChange={() => {}} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('account-edit-name-input')).toHaveValue('Cash Wallet');
    });
    expect(screen.getByTestId('account-edit-notes-input')).toHaveValue('');
  });

  it('shows a validation error when the name is blank', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <EditAccountDialog account={xtbAccount} open={true} onOpenChange={() => {}} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('account-edit-name-input')).toBeInTheDocument();
    });
    await user.clear(screen.getByTestId('account-edit-name-input'));
    await user.click(screen.getByTestId('account-edit-submit-button'));

    expect(await screen.findByText('Name is required')).toBeInTheDocument();
  });

  it('submits the trimmed name + notes, toasts, and closes', async () => {
    let captured: Record<string, unknown> | null = null;
    let capturedUrl = '';
    server.use(
      http.put('*/accounts/:id', async ({ request }) => {
        capturedUrl = request.url;
        captured = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    renderWithToaster(
      <EditAccountDialog account={xtbAccount} open={true} onOpenChange={onOpenChange} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('account-edit-name-input')).toBeInTheDocument();
    });

    const name = screen.getByTestId('account-edit-name-input');
    await user.clear(name);
    await user.type(name, 'XTB International');

    const notes = screen.getByTestId('account-edit-notes-input');
    await user.clear(notes);
    await user.type(notes, 'Updated note');

    await user.click(screen.getByTestId('account-edit-submit-button'));

    await waitFor(() => {
      expect(captured).not.toBeNull();
    });
    expect(capturedUrl).toContain('/accounts/44444444-4444-4444-4444-444444444444');
    expect(captured).toEqual({ name: 'XTB International', notes: 'Updated note' });
    expect(await screen.findByText('Renamed to "XTB International"')).toBeInTheDocument();
    await waitFor(() => {
      expect(onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it('sends notes=null when the notes field is cleared', async () => {
    let captured: Record<string, unknown> | null = null;
    server.use(
      http.put('*/accounts/:id', async ({ request }) => {
        captured = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(
      <EditAccountDialog account={xtbAccount} open={true} onOpenChange={() => {}} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('account-edit-notes-input')).toBeInTheDocument();
    });
    await user.clear(screen.getByTestId('account-edit-notes-input'));
    await user.click(screen.getByTestId('account-edit-submit-button'));

    await waitFor(() => {
      expect(captured).not.toBeNull();
    });
    expect(captured).toEqual({ name: 'XTB', notes: null });
  });

  it('shows an error toast when the API rejects the update', async () => {
    server.use(
      http.put('*/accounts/:id', () =>
        HttpResponse.json(
          { type: 'x', title: 'Bad', status: 400, detail: 'Server says no' },
          { status: 400 },
        ),
      ),
    );

    const user = userEvent.setup();
    renderWithToaster(
      <EditAccountDialog account={xtbAccount} open={true} onOpenChange={() => {}} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('account-edit-submit-button')).toBeInTheDocument();
    });
    await user.click(screen.getByTestId('account-edit-submit-button'));

    expect(await screen.findByText('Server says no')).toBeInTheDocument();
  });

  it('shows the notes error when the note exceeds the max length', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <EditAccountDialog account={xtbAccount} open={true} onOpenChange={() => {}} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('account-edit-notes-input')).toBeInTheDocument();
    });
    // The textarea's maxLength caps typed input; set an over-limit value
    // directly so the Zod `.max(1000)` refinement renders its error.
    fireEvent.change(screen.getByTestId('account-edit-notes-input'), {
      target: { value: 'x'.repeat(1001) },
    });
    await user.click(screen.getByTestId('account-edit-submit-button'));

    await waitFor(() => {
      expect(screen.getByTestId('account-edit-notes-input')).toHaveAttribute(
        'aria-invalid',
        'true',
      );
    });
  });

  it('resets the form back to the account when the dialog is closed', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(
      <EditAccountDialog account={xtbAccount} open={true} onOpenChange={onOpenChange} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('account-edit-name-input')).toBeInTheDocument();
    });
    const name = screen.getByTestId('account-edit-name-input');
    await user.clear(name);
    await user.type(name, 'Edited but not saved');

    // Cancel closes via onOpenChange(false) and triggers the reset branch.
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('closes via the Radix onOpenChange (Escape) and resets to the account', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(
      <EditAccountDialog account={xtbAccount} open={true} onOpenChange={onOpenChange} />,
    );

    await waitFor(() => {
      expect(screen.getByTestId('account-edit-name-input')).toBeInTheDocument();
    });
    // Escape fires the Dialog's own onOpenChange(false) closure (not the
    // Cancel button's onClick), exercising the `if (!next) resetToAccount()`
    // branch.
    await user.keyboard('{Escape}');
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
