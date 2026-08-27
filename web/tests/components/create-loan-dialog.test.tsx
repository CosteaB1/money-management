import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { CreateLoanDialog } from '@/src/components/loans/create-loan-dialog';
import { server } from '@/src/lib/mocks/server';
import { todayIsoUtc } from '@/src/lib/utils/date';

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

async function openDialog(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByTestId('add-loan-button'));
  await waitFor(() => {
    expect(screen.getByTestId('loan-counterparty-input')).toBeInTheDocument();
  });
}

describe('CreateLoanDialog', () => {
  it('submits a borrowed (Received) loan with no account and defaults', async () => {
    const user = userEvent.setup();

    let capturedBody: Record<string, unknown> | null = null;
    server.use(
      http.post('*/loans', async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'new-loan' }, { status: 201 });
      }),
    );

    renderWithClient(<CreateLoanDialog />);
    await openDialog(user);

    await user.type(screen.getByTestId('loan-counterparty-input'), 'Parents');
    const principal = screen.getByTestId('loan-principal-input');
    await user.clear(principal);
    await user.type(principal, '5000');

    await user.click(screen.getByTestId('loan-submit-button'));

    await waitFor(() => {
      expect(screen.queryByTestId('loan-submit-button')).not.toBeInTheDocument();
    });

    const body = capturedBody as Record<string, unknown> | null;
    expect(body).not.toBeNull();
    expect(body?.direction).toBe('Received');
    expect(body?.counterparty).toBe('Parents');
    expect(body?.principal).toBe(5000);
    expect(body?.currency).toBe('MDL');
    expect(body?.loanDate).toBe(todayIsoUtc());
    expect(body?.accountId).toBeNull();
    expect(body?.notes).toBeNull();
  });

  it('submits a lent (Given) loan with a linked account and notes', async () => {
    const user = userEvent.setup();

    let capturedBody: Record<string, unknown> | null = null;
    server.use(
      http.post('*/loans', async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'new-loan' }, { status: 201 });
      }),
    );

    renderWithClient(<CreateLoanDialog />);
    await openDialog(user);

    await user.click(screen.getByTestId('loan-direction-given'));
    await user.type(screen.getByTestId('loan-counterparty-input'), 'Ion');
    const principal = screen.getByTestId('loan-principal-input');
    await user.clear(principal);
    await user.type(principal, '2500');

    await user.click(screen.getByTestId('loan-account-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));

    await user.type(screen.getByTestId('loan-notes-input'), 'Cash from the drawer');

    await user.click(screen.getByTestId('loan-submit-button'));

    await waitFor(() => {
      expect(screen.queryByTestId('loan-submit-button')).not.toBeInTheDocument();
    });

    const body = capturedBody as Record<string, unknown> | null;
    expect(body).not.toBeNull();
    expect(body?.direction).toBe('Given');
    expect(body?.accountId).toBe('11111111-1111-1111-1111-111111111111');
    expect(body?.notes).toBe('Cash from the drawer');
  });

  it('filters the account options to non-archived accounts in the loan currency', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateLoanDialog />);
    await openDialog(user);

    // Default MDL → the three active MDL accounts, not the USD one, not
    // the archived MDL one.
    await user.click(screen.getByTestId('loan-account-select'));
    expect(await screen.findByRole('option', { name: 'No account' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Cash Wallet' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'BRD Visa' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'ING Savings' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'XTB' })).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Old Revolut' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');

    // Switch to USD → only XTB remains.
    await user.click(screen.getByTestId('loan-currency-select'));
    await user.click(await screen.findByRole('option', { name: 'USD' }));
    await user.click(screen.getByTestId('loan-account-select'));
    expect(await screen.findByRole('option', { name: 'XTB' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Cash Wallet' })).not.toBeInTheDocument();
  });

  it('shows the no-accounts hint for a currency without matching accounts', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateLoanDialog />);
    await openDialog(user);

    await user.click(screen.getByTestId('loan-currency-select'));
    await user.click(await screen.findByRole('option', { name: 'RON' }));
    await user.click(screen.getByTestId('loan-account-select'));
    expect(await screen.findByText('No RON accounts available.')).toBeInTheDocument();
  });

  it('resets an incompatible account selection when the currency changes', async () => {
    const user = userEvent.setup();

    let capturedBody: Record<string, unknown> | null = null;
    server.use(
      http.post('*/loans', async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'new-loan' }, { status: 201 });
      }),
    );

    renderWithClient(<CreateLoanDialog />);
    await openDialog(user);

    // Pick an MDL account, then flip the loan currency to USD.
    await user.click(screen.getByTestId('loan-account-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));
    expect(screen.getByTestId('loan-account-select')).toHaveTextContent('Cash Wallet');

    await user.click(screen.getByTestId('loan-currency-select'));
    await user.click(await screen.findByRole('option', { name: 'USD' }));

    // The stale MDL selection is dropped back to the "No account" default.
    expect(screen.getByTestId('loan-account-select')).toHaveTextContent('No account');

    await user.type(screen.getByTestId('loan-counterparty-input'), 'Maria');
    const principal = screen.getByTestId('loan-principal-input');
    await user.clear(principal);
    await user.type(principal, '1000');
    await user.click(screen.getByTestId('loan-submit-button'));

    await waitFor(() => expect(capturedBody).not.toBeNull());
    expect((capturedBody as Record<string, unknown> | null)?.accountId).toBeNull();
    expect((capturedBody as Record<string, unknown> | null)?.currency).toBe('USD');
  });

  it('keeps a still-compatible account selection when the currency re-selects the same value', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateLoanDialog />);
    await openDialog(user);

    await user.click(screen.getByTestId('loan-account-select'));
    await user.click(await screen.findByRole('option', { name: 'ING Savings' }));

    // Re-picking MDL keeps the matching selection in place.
    await user.click(screen.getByTestId('loan-currency-select'));
    await user.click(await screen.findByRole('option', { name: 'MDL' }));
    expect(screen.getByTestId('loan-account-select')).toHaveTextContent('ING Savings');
  });

  it('shows the inline counterparty error', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateLoanDialog />);
    await openDialog(user);

    const principal = screen.getByTestId('loan-principal-input');
    await user.clear(principal);
    await user.type(principal, '100');
    await user.click(screen.getByTestId('loan-submit-button'));

    expect(await screen.findByText('Counterparty is required')).toBeInTheDocument();
  });

  it('validates that the principal is positive', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateLoanDialog />);
    await openDialog(user);

    await user.type(screen.getByTestId('loan-counterparty-input'), 'Somebody');
    // Default principal is 0 — should fail positive() validation.
    await user.click(screen.getByTestId('loan-submit-button'));

    expect(await screen.findByText('Principal must be greater than 0')).toBeInTheDocument();
  });

  it('refuses a future loan date', async () => {
    const user = userEvent.setup();

    let postCalled = false;
    server.use(
      http.post('*/loans', () => {
        postCalled = true;
        return HttpResponse.json({ id: 'should-not-create' }, { status: 201 });
      }),
    );

    renderWithClient(<CreateLoanDialog />);
    await openDialog(user);

    await user.type(screen.getByTestId('loan-counterparty-input'), 'Future');
    const principal = screen.getByTestId('loan-principal-input');
    await user.clear(principal);
    await user.type(principal, '100');

    // type=date is finicky in jsdom + userEvent — remove the max constraint
    // so jsdom accepts a future value, then drive it via fireEvent.change so
    // RHF picks it up through the registered change handler.
    const dateInput = screen.getByTestId('loan-date-input') as HTMLInputElement;
    dateInput.removeAttribute('max');
    fireEvent.change(dateInput, { target: { value: '2100-01-01' } });

    await user.click(screen.getByTestId('loan-submit-button'));

    expect(await screen.findByText('Loan date cannot be in the future')).toBeInTheDocument();
    expect(postCalled).toBe(false);
  });

  it('rejects notes longer than 500 characters', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateLoanDialog />);
    await openDialog(user);

    await user.type(screen.getByTestId('loan-counterparty-input'), 'Notes overflow');
    const principal = screen.getByTestId('loan-principal-input');
    await user.clear(principal);
    await user.type(principal, '100');

    // The textarea clamps at 500 via maxLength — strip it and drive the value
    // via fireEvent so the Zod guard (the server-mirroring layer) is exercised.
    const notes = screen.getByTestId('loan-notes-input') as HTMLTextAreaElement;
    notes.removeAttribute('maxlength');
    fireEvent.change(notes, { target: { value: 'x'.repeat(501) } });

    await user.click(screen.getByTestId('loan-submit-button'));

    expect(await screen.findByText('Notes must be 500 characters or less')).toBeInTheDocument();
  });

  it('resets the form when the dialog is dismissed without submitting', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateLoanDialog />);
    await openDialog(user);

    await user.type(screen.getByTestId('loan-counterparty-input'), 'Draft person');
    await user.keyboard('{Escape}');
    await waitFor(() => {
      expect(screen.queryByTestId('loan-counterparty-input')).not.toBeInTheDocument();
    });

    // Reopening shows a clean form — the Escape-close path ran reset().
    await openDialog(user);
    expect((screen.getByTestId('loan-counterparty-input') as HTMLInputElement).value).toBe('');
  });

  it('surfaces an ApiError inline and keeps the dialog open', async () => {
    server.use(
      http.post('*/loans', () =>
        HttpResponse.json({ detail: 'Account currency mismatch' }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<CreateLoanDialog />);
    await openDialog(user);

    await user.type(screen.getByTestId('loan-counterparty-input'), 'Parents');
    const principal = screen.getByTestId('loan-principal-input');
    await user.clear(principal);
    await user.type(principal, '5000');
    await user.click(screen.getByTestId('loan-submit-button'));

    const error = await screen.findByTestId('create-loan-error');
    expect(error).toHaveTextContent('Account currency mismatch');
    expect(error).toHaveAttribute('role', 'alert');
    // Dialog stays open so the user can recover.
    expect(screen.getByTestId('loan-submit-button')).toBeInTheDocument();
  });

  it('falls back to an error toast on a network failure', async () => {
    const { Toaster } = await import('@/src/components/ui/sonner');
    server.use(http.post('*/loans', () => HttpResponse.error()));
    const user = userEvent.setup();
    render(
      <QueryClientProvider
        client={
          new QueryClient({
            defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
          })
        }
      >
        <CreateLoanDialog />
        <Toaster />
      </QueryClientProvider>,
    );
    await openDialog(user);

    await user.type(screen.getByTestId('loan-counterparty-input'), 'Parents');
    const principal = screen.getByTestId('loan-principal-input');
    await user.clear(principal);
    await user.type(principal, '5000');
    await user.click(screen.getByTestId('loan-submit-button'));

    expect(await screen.findByText(/fetch/i)).toBeInTheDocument();
  });
});
