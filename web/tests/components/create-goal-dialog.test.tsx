import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { CreateGoalDialog } from '@/src/components/goals/create-goal-dialog';
import { server } from '@/src/lib/mocks/server';

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('CreateGoalDialog', () => {
  it('submits a manual goal (no linked account) and closes on success', async () => {
    const user = userEvent.setup();

    let capturedBody: Record<string, unknown> | null = null;
    server.use(
      http.post('*/goals', async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'new-goal' }, { status: 201 });
      }),
    );

    renderWithClient(<CreateGoalDialog />);

    await user.click(screen.getByTestId('add-goal-button'));

    await waitFor(() => {
      expect(screen.getByTestId('goal-name-input')).toBeInTheDocument();
    });

    await user.type(screen.getByTestId('goal-name-input'), 'Emergency fund');
    const target = screen.getByTestId('goal-target-amount-input');
    await user.clear(target);
    await user.type(target, '50000');

    // Manual is the default mode — no extra field needed.
    await user.click(screen.getByTestId('goal-submit-button'));

    await waitFor(() => {
      expect(screen.queryByTestId('goal-submit-button')).not.toBeInTheDocument();
    });

    const body = capturedBody as Record<string, unknown> | null;
    expect(body).not.toBeNull();
    expect(body?.name).toBe('Emergency fund');
    expect(body?.targetAmount).toBe(50000);
    expect(body?.linkedAccountId).toBeUndefined();
  });

  it('submits a linked goal with the chosen account id in the payload', async () => {
    const user = userEvent.setup();

    let capturedBody: Record<string, unknown> | null = null;
    server.use(
      http.post('*/goals', async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'new-goal' }, { status: 201 });
      }),
    );

    renderWithClient(<CreateGoalDialog />);

    await user.click(screen.getByTestId('add-goal-button'));

    await waitFor(() => {
      expect(screen.getByTestId('goal-name-input')).toBeInTheDocument();
    });

    await user.type(screen.getByTestId('goal-name-input'), 'House down payment');
    const target = screen.getByTestId('goal-target-amount-input');
    await user.clear(target);
    await user.type(target, '200000');

    // Switch to linked mode — Account select shows up.
    await user.click(screen.getByTestId('goal-mode-linked'));

    await waitFor(() => {
      expect(screen.getByTestId('goal-linked-account-select')).toBeInTheDocument();
    });

    await user.click(screen.getByTestId('goal-linked-account-select'));
    const ingOption = await screen.findByRole('option', { name: /ING Savings/ });
    await user.click(ingOption);

    await user.click(screen.getByTestId('goal-submit-button'));

    await waitFor(() => {
      expect(screen.queryByTestId('goal-submit-button')).not.toBeInTheDocument();
    });

    const body = capturedBody as Record<string, unknown> | null;
    expect(body).not.toBeNull();
    expect(body?.name).toBe('House down payment');
    expect(body?.linkedAccountId).toBe('33333333-3333-3333-3333-333333333333');
  });

  it('shows the inline error when linked mode is picked but no account selected', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateGoalDialog />);

    await user.click(screen.getByTestId('add-goal-button'));

    await waitFor(() => {
      expect(screen.getByTestId('goal-name-input')).toBeInTheDocument();
    });

    await user.type(screen.getByTestId('goal-name-input'), 'Investment goal');
    const target = screen.getByTestId('goal-target-amount-input');
    await user.clear(target);
    await user.type(target, '15000');

    await user.click(screen.getByTestId('goal-mode-linked'));

    await user.click(screen.getByTestId('goal-submit-button'));

    await waitFor(() => {
      expect(screen.getByText(/pick an account/i)).toBeInTheDocument();
    });

    // Dialog stays open so the user can recover.
    expect(screen.getByTestId('goal-submit-button')).toBeInTheDocument();
  });

  it('refuses a past target date', async () => {
    const user = userEvent.setup();

    // Catch any POST so we can assert the submission was BLOCKED client-side.
    let postCalled = false;
    server.use(
      http.post('*/goals', () => {
        postCalled = true;
        return HttpResponse.json({ id: 'should-not-create' }, { status: 201 });
      }),
    );

    renderWithClient(<CreateGoalDialog />);

    await user.click(screen.getByTestId('add-goal-button'));

    await waitFor(() => {
      expect(screen.getByTestId('goal-name-input')).toBeInTheDocument();
    });

    await user.type(screen.getByTestId('goal-name-input'), 'Past goal');
    const target = screen.getByTestId('goal-target-amount-input');
    await user.clear(target);
    await user.type(target, '5000');

    // type=date is finicky in jsdom + userEvent — remove the min constraint so
    // jsdom accepts a past value, then drive it via fireEvent.change so RHF
    // picks it up through the registered change handler.
    const dateInput = screen.getByTestId('goal-target-date-input') as HTMLInputElement;
    dateInput.removeAttribute('min');
    fireEvent.change(dateInput, { target: { value: '2020-01-01' } });

    await user.click(screen.getByTestId('goal-submit-button'));

    expect(await screen.findByText('Target date cannot be in the past')).toBeInTheDocument();
    expect(postCalled).toBe(false);
  });

  it('validates that target amount is positive', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateGoalDialog />);

    await user.click(screen.getByTestId('add-goal-button'));

    await waitFor(() => {
      expect(screen.getByTestId('goal-submit-button')).toBeInTheDocument();
    });

    await user.type(screen.getByTestId('goal-name-input'), 'Some goal');
    // Default targetAmount is 0 — should fail positive() validation.
    await user.click(screen.getByTestId('goal-submit-button'));

    await waitFor(() => {
      expect(screen.getByText(/target amount must be greater than 0/i)).toBeInTheDocument();
    });
  });

  it('toggling to linked then back to manual hides the account select', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateGoalDialog />);
    await user.click(screen.getByTestId('add-goal-button'));
    await waitFor(() => expect(screen.getByTestId('goal-mode-linked')).toBeInTheDocument());

    await user.click(screen.getByTestId('goal-mode-linked'));
    expect(await screen.findByTestId('goal-linked-account-select')).toBeInTheDocument();

    await user.click(screen.getByTestId('goal-mode-manual'));
    await waitFor(() => {
      expect(screen.queryByTestId('goal-linked-account-select')).not.toBeInTheDocument();
    });
  });

  it('submits a linked goal with the chosen account', async () => {
    let body: Record<string, unknown> | null = null;
    server.use(
      http.post('*/goals', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'g-new' }, { status: 201 });
      }),
    );
    const user = userEvent.setup();
    renderWithClient(<CreateGoalDialog />);
    await user.click(screen.getByTestId('add-goal-button'));
    await user.type(screen.getByTestId('goal-name-input'), 'Brokerage goal');
    const target = screen.getByTestId('goal-target-amount-input');
    await user.clear(target);
    await user.type(target, '15000');
    await user.click(screen.getByTestId('goal-mode-linked'));
    await user.click(await screen.findByTestId('goal-linked-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB' }));
    await user.click(screen.getByTestId('goal-submit-button'));
    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ linkedAccountId: '44444444-4444-4444-4444-444444444444' });
  });

  it('shows the inline name error', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateGoalDialog />);
    await user.click(screen.getByTestId('add-goal-button'));
    const t = screen.getByTestId('goal-target-amount-input');
    await user.clear(t);
    await user.type(t, '15000');
    await user.click(screen.getByTestId('goal-submit-button'));
    expect(await screen.findByText('Name is required')).toBeInTheDocument();
  });

  it('surfaces a 404 on the linked-account field', async () => {
    server.use(http.post('*/goals', () => HttpResponse.json({ detail: 'gone' }, { status: 404 })));
    const user = userEvent.setup();
    renderWithClient(<CreateGoalDialog />);
    await user.click(screen.getByTestId('add-goal-button'));
    await user.type(screen.getByTestId('goal-name-input'), 'Linked');
    const t = screen.getByTestId('goal-target-amount-input');
    await user.clear(t);
    await user.type(t, '15000');
    await user.click(screen.getByTestId('goal-mode-linked'));
    await user.click(await screen.findByTestId('goal-linked-account-select'));
    await user.click(await screen.findByRole('option', { name: 'XTB' }));
    await user.click(screen.getByTestId('goal-submit-button'));
    expect(await screen.findByText('Linked account not found.')).toBeInTheDocument();
  });

  it('shows a generic error toast for a non-404 failure', async () => {
    const { Toaster } = await import('@/src/components/ui/sonner');
    server.use(http.post('*/goals', () => HttpResponse.json({ detail: 'boom' }, { status: 500 })));
    const user = userEvent.setup();
    render(
      <QueryClientProvider
        client={
          new QueryClient({
            defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
          })
        }
      >
        <CreateGoalDialog />
        <Toaster />
      </QueryClientProvider>,
    );
    await user.click(screen.getByTestId('add-goal-button'));
    await user.type(screen.getByTestId('goal-name-input'), 'Manual');
    const t = screen.getByTestId('goal-target-amount-input');
    await user.clear(t);
    await user.type(t, '15000');
    await user.click(screen.getByTestId('goal-submit-button'));
    expect(await screen.findByText('boom')).toBeInTheDocument();
  });

  it('shows "No accounts available" in linked mode when there are no accounts', async () => {
    server.use(http.get('*/accounts', () => HttpResponse.json([])));
    const user = userEvent.setup();
    renderWithClient(<CreateGoalDialog />);
    await user.click(screen.getByTestId('add-goal-button'));
    await user.click(await screen.findByTestId('goal-mode-linked'));
    await user.click(await screen.findByTestId('goal-linked-account-select'));
    expect(await screen.findByText('No accounts available.')).toBeInTheDocument();
  });
});
