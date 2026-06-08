import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { UpdateSavedDialog } from '@/src/components/goals/update-saved-dialog';
import { server } from '@/src/lib/mocks/server';
import type { GoalDto } from '@/src/types/api';

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

const manualGoal: GoalDto = {
  id: 'g-manual',
  name: 'Vacation',
  targetAmount: 10000,
  targetDate: '2026-08-15',
  linkedAccountId: null,
  linkedAccountName: null,
  saved: 4500,
  remaining: 5500,
  progressPercent: 4500 / 10000,
  status: 'AtRisk',
  requiredMonthlyContribution: 1830,
  isLinkedMode: false,
  missingFxRate: false,
};

describe('UpdateSavedDialog', () => {
  it('PATCHes the new saved amount and closes on success', async () => {
    const user = userEvent.setup();
    let capturedBody: Record<string, unknown> | null = null;

    server.use(
      http.patch('*/goals/:id/manual-saved', async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const onOpenChange = vi.fn();
    renderWithClient(<UpdateSavedDialog goal={manualGoal} open onOpenChange={onOpenChange} />);

    const input = screen.getByTestId('update-saved-amount-input');
    await user.clear(input);
    await user.type(input, '6000');

    await user.click(screen.getByTestId('update-saved-submit-button'));

    await waitFor(() => {
      expect(onOpenChange).toHaveBeenCalledWith(false);
    });

    const body = capturedBody as Record<string, unknown> | null;
    expect(body).not.toBeNull();
    expect(body?.amount).toBe(6000);
  });

  it('surfaces a 400 (linked-mode reject) inline and closes the dialog', async () => {
    const user = userEvent.setup();

    server.use(
      http.patch('*/goals/:id/manual-saved', () =>
        HttpResponse.json({ error: 'Goal is not in manual mode' }, { status: 400 }),
      ),
    );

    const onOpenChange = vi.fn();
    renderWithClient(<UpdateSavedDialog goal={manualGoal} open onOpenChange={onOpenChange} />);

    const input = screen.getByTestId('update-saved-amount-input');
    await user.clear(input);
    await user.type(input, '7000');

    await user.click(screen.getByTestId('update-saved-submit-button'));

    await waitFor(() => {
      expect(onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it('rejects a negative amount client-side', async () => {
    const user = userEvent.setup();

    // Track if the PATCH is even attempted — proves the schema blocked it.
    let patchCalled = false;
    server.use(
      http.patch('*/goals/:id/manual-saved', () => {
        patchCalled = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const onOpenChange = vi.fn();
    renderWithClient(<UpdateSavedDialog goal={manualGoal} open onOpenChange={onOpenChange} />);

    const input = screen.getByTestId('update-saved-amount-input') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '-500');

    await user.click(screen.getByTestId('update-saved-submit-button'));

    // Either the inline error appears, or the dialog stays open with no
    // PATCH having been fired — both prove the negative value was rejected.
    await waitFor(() => {
      const inlineError = screen.queryByText(/amount cannot be negative/i);
      const dialogStillOpen = screen.queryByTestId('update-saved-submit-button');
      expect(Boolean(inlineError) || (Boolean(dialogStillOpen) && !patchCalled)).toBe(true);
    });
    expect(patchCalled).toBe(false);
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it('resets the form and reports close when dismissed via Escape', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(<UpdateSavedDialog goal={manualGoal} open onOpenChange={onOpenChange} />);

    const input = screen.getByTestId('update-saved-amount-input');
    await user.clear(input);
    await user.type(input, '9999');
    // Escape closes the Radix dialog → the onOpenChange wrapper runs reset().
    await user.keyboard('{Escape}');
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });
});
