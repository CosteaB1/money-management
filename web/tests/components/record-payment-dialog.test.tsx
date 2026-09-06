import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { RecordPaymentDialog } from '@/src/components/loans/record-payment-dialog';
import { server } from '@/src/lib/mocks/server';
import { formatMoney } from '@/src/lib/utils/currency';
import { todayIsoUtc } from '@/src/lib/utils/date';
import type { LoanDto } from '@/src/types/api';

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

// Received (borrowed) EUR loan — no matching accounts exist in the seed.
const eurLoan: LoanDto = {
  id: 'l0000001-0000-0000-0000-000000000001',
  direction: 'Received',
  counterparty: 'Parents',
  principal: 5000,
  currency: 'EUR',
  loanDate: '2026-01-10',
  totalRepaid: 2000,
  outstanding: 3000,
  outstandingMdl: 57600,
  missingFxRate: false,
  status: 'Active',
  paymentCount: 2,
  isAccountLinked: false,
  notes: null,
};

// Given (lent) MDL loan — the seeded MDL accounts are offerable.
const mdlLoan: LoanDto = {
  id: 'l0000001-0000-0000-0000-000000000002',
  direction: 'Given',
  counterparty: 'Ion',
  principal: 2500,
  currency: 'MDL',
  loanDate: '2026-03-05',
  totalRepaid: 500,
  outstanding: 2000,
  outstandingMdl: 2000,
  missingFxRate: false,
  status: 'Active',
  paymentCount: 1,
  isAccountLinked: true,
  notes: null,
};

describe('RecordPaymentDialog', () => {
  it('shows the borrowed-direction context line with the outstanding balance', async () => {
    renderWithClient(<RecordPaymentDialog loan={eurLoan} open onOpenChange={vi.fn()} />);

    const context = await screen.findByTestId('record-payment-context');
    expect(context).toHaveTextContent('Repaying loan from Parents');
    expect(context.textContent).toContain(formatMoney(3000, 'EUR'));
  });

  it('shows the lent-direction context line', async () => {
    renderWithClient(<RecordPaymentDialog loan={mdlLoan} open onOpenChange={vi.fn()} />);

    const context = await screen.findByTestId('record-payment-context');
    expect(context).toHaveTextContent('Recording repayment from Ion');
  });

  it('rejects an amount above the outstanding balance', async () => {
    const user = userEvent.setup();

    let postCalled = false;
    server.use(
      http.post('*/loans/:id/payments', () => {
        postCalled = true;
        return HttpResponse.json({ id: 'nope' }, { status: 201 });
      }),
    );

    renderWithClient(<RecordPaymentDialog loan={eurLoan} open onOpenChange={vi.fn()} />);

    const amount = screen.getByTestId('payment-amount-input');
    // The input also carries a native max={outstanding} constraint, which
    // (in jsdom as in real browsers) blocks submission before RHF runs.
    // Strip it so the Zod layer's styled message is what we exercise —
    // mirrors the create-goal-dialog date-test convention.
    amount.removeAttribute('max');
    await user.clear(amount);
    await user.type(amount, '5000');
    await user.click(screen.getByTestId('payment-submit-button'));

    expect(await screen.findByText(/amount cannot exceed the outstanding/i)).toBeInTheDocument();
    expect(postCalled).toBe(false);
  });

  it('rejects a non-positive amount', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordPaymentDialog loan={eurLoan} open onOpenChange={vi.fn()} />);

    // Default amount is 0 — should fail positive() validation.
    await user.click(screen.getByTestId('payment-submit-button'));

    expect(await screen.findByText('Amount must be greater than 0')).toBeInTheDocument();
  });

  it('rejects a payment date before the loan date', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordPaymentDialog loan={eurLoan} open onOpenChange={vi.fn()} />);

    const amount = screen.getByTestId('payment-amount-input');
    await user.clear(amount);
    await user.type(amount, '500');

    // type=date is finicky in jsdom + userEvent — remove the min constraint
    // then drive it via fireEvent.change so RHF sees the value.
    const dateInput = screen.getByTestId('payment-date-input') as HTMLInputElement;
    dateInput.removeAttribute('min');
    fireEvent.change(dateInput, { target: { value: '2026-01-05' } });

    await user.click(screen.getByTestId('payment-submit-button'));

    expect(
      await screen.findByText('Payment date cannot be before the loan date'),
    ).toBeInTheDocument();
  });

  it('rejects a payment date in the future', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordPaymentDialog loan={eurLoan} open onOpenChange={vi.fn()} />);

    const amount = screen.getByTestId('payment-amount-input');
    await user.clear(amount);
    await user.type(amount, '500');

    const dateInput = screen.getByTestId('payment-date-input') as HTMLInputElement;
    dateInput.removeAttribute('max');
    fireEvent.change(dateInput, { target: { value: '2100-01-01' } });

    await user.click(screen.getByTestId('payment-submit-button'));

    expect(await screen.findByText('Payment date cannot be in the future')).toBeInTheDocument();
  });

  it('submits an untracked payment with today as the default date', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();

    let capturedBody: Record<string, unknown> | null = null;
    let capturedUrl = '';
    server.use(
      http.post('*/loans/:id/payments', async ({ request }) => {
        capturedUrl = request.url;
        capturedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'new-payment' }, { status: 201 });
      }),
    );

    renderWithClient(<RecordPaymentDialog loan={eurLoan} open onOpenChange={onOpenChange} />);

    const amount = screen.getByTestId('payment-amount-input');
    await user.clear(amount);
    await user.type(amount, '500');
    await user.click(screen.getByTestId('payment-submit-button'));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(capturedUrl).toContain(`/loans/${eurLoan.id}/payments`);
    const body = capturedBody as Record<string, unknown> | null;
    expect(body).not.toBeNull();
    expect(body?.amount).toBe(500);
    expect(body?.occurredOn).toBe(todayIsoUtc());
    expect(body?.accountId).toBeNull();
    expect(body?.notes).toBeNull();
  });

  it('submits an account-linked payment with notes', async () => {
    const user = userEvent.setup();

    let capturedBody: Record<string, unknown> | null = null;
    server.use(
      http.post('*/loans/:id/payments', async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'new-payment' }, { status: 201 });
      }),
    );

    renderWithClient(<RecordPaymentDialog loan={mdlLoan} open onOpenChange={vi.fn()} />);

    const amount = screen.getByTestId('payment-amount-input');
    await user.clear(amount);
    await user.type(amount, '1000');

    await user.click(screen.getByTestId('payment-account-select'));
    await user.click(await screen.findByRole('option', { name: 'Cash Wallet' }));

    await user.type(screen.getByTestId('payment-notes-input'), 'Half of it back');
    await user.click(screen.getByTestId('payment-submit-button'));

    await waitFor(() => expect(capturedBody).not.toBeNull());
    const body = capturedBody as Record<string, unknown> | null;
    expect(body?.accountId).toBe('11111111-1111-1111-1111-111111111111');
    expect(body?.notes).toBe('Half of it back');
  });

  it('filters account options by the loan currency', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordPaymentDialog loan={mdlLoan} open onOpenChange={vi.fn()} />);

    await user.click(screen.getByTestId('payment-account-select'));
    expect(await screen.findByRole('option', { name: 'Cash Wallet' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'No account' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'XTB' })).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Old Revolut' })).not.toBeInTheDocument();
  });

  it('shows the no-accounts hint when no account matches the loan currency', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordPaymentDialog loan={eurLoan} open onOpenChange={vi.fn()} />);

    await user.click(screen.getByTestId('payment-account-select'));
    expect(await screen.findByText('No EUR accounts available.')).toBeInTheDocument();
  });

  it('rejects notes longer than 500 characters', async () => {
    const user = userEvent.setup();
    renderWithClient(<RecordPaymentDialog loan={mdlLoan} open onOpenChange={vi.fn()} />);

    const amount = screen.getByTestId('payment-amount-input');
    await user.clear(amount);
    await user.type(amount, '100');

    // The textarea clamps at 500 via maxLength — strip it and drive the value
    // via fireEvent so the Zod guard (the server-mirroring layer) is exercised.
    const notes = screen.getByTestId('payment-notes-input') as HTMLTextAreaElement;
    notes.removeAttribute('maxlength');
    fireEvent.change(notes, { target: { value: 'x'.repeat(501) } });

    await user.click(screen.getByTestId('payment-submit-button'));

    expect(await screen.findByText('Notes must be 500 characters or less')).toBeInTheDocument();
  });

  it('surfaces an ApiError inline and keeps the dialog open', async () => {
    server.use(
      http.post('*/loans/:id/payments', () =>
        HttpResponse.json({ detail: 'Amount exceeds the outstanding balance' }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(<RecordPaymentDialog loan={eurLoan} open onOpenChange={onOpenChange} />);

    const amount = screen.getByTestId('payment-amount-input');
    await user.clear(amount);
    await user.type(amount, '500');
    await user.click(screen.getByTestId('payment-submit-button'));

    const error = await screen.findByTestId('record-payment-error');
    expect(error).toHaveTextContent('Amount exceeds the outstanding balance');
    expect(error).toHaveAttribute('role', 'alert');
    expect(onOpenChange).not.toHaveBeenCalled();
  });

  it('falls back to an error toast on a network failure', async () => {
    const { Toaster } = await import('@/src/components/ui/sonner');
    server.use(http.post('*/loans/:id/payments', () => HttpResponse.error()));
    const user = userEvent.setup();
    render(
      <QueryClientProvider
        client={
          new QueryClient({
            defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
          })
        }
      >
        <RecordPaymentDialog loan={eurLoan} open onOpenChange={vi.fn()} />
        <Toaster />
      </QueryClientProvider>,
    );

    const amount = screen.getByTestId('payment-amount-input');
    await user.clear(amount);
    await user.type(amount, '500');
    await user.click(screen.getByTestId('payment-submit-button'));

    expect(await screen.findByText(/fetch/i)).toBeInTheDocument();
  });
});
