import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { ExportBackupCard } from '@/src/components/settings/export-backup-card';
import {
  errorMessage,
  ImportBackupCard,
  summarizeImport,
} from '@/src/components/settings/import-backup-card';
import { Toaster } from '@/src/components/ui/sonner';
import { getBackupDownloadUrl } from '@/src/lib/api/data';
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

function makeBackupFile(contents = '{"schemaVersion":1}') {
  return new File([contents], 'backup.json', { type: 'application/json' });
}

describe('Data settings — Export', () => {
  it('builds the export download URL from the API base', () => {
    expect(getBackupDownloadUrl('http://api.test')).toBe('http://api.test/data/export');
  });

  it('renders a download button', () => {
    renderWithClient(<ExportBackupCard />);
    expect(screen.getByTestId('download-backup-button')).toBeInTheDocument();
  });
});

describe('Data settings — Import', () => {
  it('renders the import card with a disabled restore action until a file is chosen', () => {
    renderWithClient(<ImportBackupCard />);

    expect(screen.getByTestId('import-backup-card')).toBeInTheDocument();
    expect(screen.getByTestId('restore-backup-button')).toBeDisabled();
  });

  it('enables the restore action once a .json file is selected', async () => {
    const user = userEvent.setup();
    renderWithClient(<ImportBackupCard />);

    await user.upload(screen.getByTestId('backup-file-input'), makeBackupFile());

    expect(screen.getByTestId('restore-backup-button')).toBeEnabled();
    expect(screen.getByTestId('selected-backup-name')).toHaveTextContent('backup.json');
  });

  it('opens a destructive confirmation dialog warning that all data is replaced', async () => {
    const user = userEvent.setup();
    renderWithClient(<ImportBackupCard />);

    await user.upload(screen.getByTestId('backup-file-input'), makeBackupFile());
    await user.click(screen.getByTestId('restore-backup-button'));

    await waitFor(() => {
      expect(screen.getByTestId('restore-confirm-dialog')).toBeInTheDocument();
    });

    const warning = screen.getByTestId('restore-confirm-warning');
    expect(warning).toHaveTextContent(/permanently REPLACES ALL current data/i);
    expect(warning).toHaveTextContent(/cannot be undone/i);
  });

  it('calls the import endpoint on confirm and toasts the restored counts', async () => {
    let importCalled = false;
    server.use(
      http.post('*/data/import', async () => {
        importCalled = true;
        return HttpResponse.json({
          schemaVersion: 2,
          accounts: 4,
          categories: 9,
          transactions: 128,
          importBatches: 3,
          budgets: 6,
          budgetPeriods: 6,
          savingsGoals: 2,
          savingsGoalContributions: 7,
        });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<ImportBackupCard />);

    await user.upload(screen.getByTestId('backup-file-input'), makeBackupFile());
    await user.click(screen.getByTestId('restore-backup-button'));

    await waitFor(() => {
      expect(screen.getByTestId('restore-confirm-button')).toBeInTheDocument();
    });
    await user.click(screen.getByTestId('restore-confirm-button'));

    await waitFor(() => {
      expect(importCalled).toBe(true);
    });

    // Success toast summarizes the per-table counts (schemaVersion excluded).
    await waitFor(() => {
      expect(
        screen.getByText(
          /Restored 4 accounts, 9 categories, 128 transactions, 3 import batches, 6 budgets, 6 budget periods, 2 savings goals, 7 savings goal contributions\./i,
        ),
      ).toBeInTheDocument();
    });

    // Picker resets after a successful restore.
    await waitFor(() => {
      expect(screen.queryByTestId('selected-backup-name')).not.toBeInTheDocument();
    });
  });

  it('surfaces the API error message on an unsupported schema version', async () => {
    server.use(
      http.post('*/data/import', async () =>
        HttpResponse.json({ error: 'Unsupported backup schema version: 99' }, { status: 400 }),
      ),
    );

    const user = userEvent.setup();
    renderWithClient(<ImportBackupCard />);

    await user.upload(screen.getByTestId('backup-file-input'), makeBackupFile());
    await user.click(screen.getByTestId('restore-backup-button'));

    await waitFor(() => {
      expect(screen.getByTestId('restore-confirm-button')).toBeInTheDocument();
    });
    await user.click(screen.getByTestId('restore-confirm-button'));

    // Inline error + toast both carry the backend message.
    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent('Unsupported backup schema version: 99');
    });
    expect(screen.getAllByText('Unsupported backup schema version: 99').length).toBeGreaterThan(0);
  });

  it('surfaces a generic message on a network-level failure (non-ApiError)', async () => {
    // HttpResponse.error() makes fetch reject with a TypeError — exercises the
    // `err instanceof Error` arm of errorMessage().
    server.use(http.post('*/data/import', () => HttpResponse.error()));
    const user = userEvent.setup();
    renderWithClient(<ImportBackupCard />);
    await user.upload(screen.getByTestId('backup-file-input'), makeBackupFile());
    await user.click(screen.getByTestId('restore-backup-button'));
    await user.click(await screen.findByTestId('restore-confirm-button'));
    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeInTheDocument();
    });
  });

  it('blocks closing the confirm dialog while a restore is in flight', async () => {
    server.use(
      http.post('*/data/import', () => new Promise(() => {}) as unknown as Promise<Response>),
    );
    const user = userEvent.setup();
    renderWithClient(<ImportBackupCard />);
    await user.upload(screen.getByTestId('backup-file-input'), makeBackupFile());
    await user.click(screen.getByTestId('restore-backup-button'));
    await user.click(await screen.findByTestId('restore-confirm-button'));
    // Mid-flight: Escape must NOT close the dialog (the onOpenChange guard).
    await user.keyboard('{Escape}');
    expect(screen.getByTestId('restore-confirm-dialog')).toBeInTheDocument();
  });

  it('closes the confirm dialog on Escape when no restore is in flight', async () => {
    const user = userEvent.setup();
    renderWithClient(<ImportBackupCard />);
    await user.upload(screen.getByTestId('backup-file-input'), makeBackupFile());
    await user.click(screen.getByTestId('restore-backup-button'));
    expect(await screen.findByTestId('restore-confirm-dialog')).toBeInTheDocument();
    // Not pending → Escape closes the dialog (runs setConfirmOpen(false)).
    await user.keyboard('{Escape}');
    await waitFor(() =>
      expect(screen.queryByTestId('restore-confirm-dialog')).not.toBeInTheDocument(),
    );
  });
});

describe('summarizeImport', () => {
  it('returns the fallback when there are no numeric counts', () => {
    expect(summarizeImport({ schemaVersion: 1 } as never)).toBe('Restore complete.');
  });
});

describe('errorMessage', () => {
  it('uses the Error message', () => {
    expect(errorMessage(new Error('boom'))).toBe('boom');
  });

  it('returns the friendly fallback for a non-Error throw', () => {
    expect(errorMessage('weird string')).toBe(
      'Failed to restore the backup. Check the file and try again.',
    );
  });
});
