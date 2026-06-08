import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { ImportPreview } from '@/src/components/transactions/import-preview';
import { server } from '@/src/lib/mocks/server';
import type { StatementPreviewDto } from '@/src/types/api';

// `ImportPreview` calls `useRouter().push` on a successful commit. The default
// next/navigation stub in jsdom blows up without an App-Router context, so we
// mock the hook to a noop. The redirect is not part of any assertion here.
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

// MAIB Checking (Cash Wallet stand-in) is the import account in these tests; the
// other MDL accounts (ING Savings, BRD Visa) are the eligible counters.
const IMPORT_ACCOUNT_ID = '11111111-1111-1111-1111-111111111111';
const ING_SAVINGS_ID = '33333333-3333-3333-3333-333333333333';
// XTB is a USD Brokerage in the mock seed — a cross-currency counter for the
// MDL import account.
const XTB_USD_ID = '44444444-4444-4444-4444-444444444444';

// Expense categories seeded in the mock handlers (web/tests/mocks/handlers.ts).
const GROCERIES_ID = 'c0000001-0000-0000-0000-000000000001';
const DINING_OUT_ID = 'c0000001-0000-0000-0000-000000000002';

function buildPreview(): StatementPreviewDto {
  return {
    fileHash: 'hash-abc',
    statementPeriod: { from: '2025-05-01', to: '2025-05-31' },
    bankSource: 'Maib',
    summary: {
      openingBalance: 1000,
      closingBalance: 1200,
      totalIn: 500,
      totalOut: 300,
      // 1000 + 500 − 300 = 1200 ties to closing (fees live inside Out).
      totalFees: 0,
    },
    transactions: [
      {
        // Row 0: transfer-flagged (e.g. ATM withdrawal heading to Cash).
        transactionDate: '2025-05-04',
        direction: 'Expense',
        amount: 500,
        description: 'ATM withdrawal',
        isDuplicate: false,
        isTransfer: true,
      },
      {
        // Row 1: a regular non-transfer expense — picker should never appear.
        transactionDate: '2025-05-12',
        direction: 'Expense',
        amount: 35,
        description: 'Coffee at Tucano',
        isDuplicate: false,
        isTransfer: false,
      },
    ],
  };
}

/** Wait until accounts have hydrated so `counterAccountOptions` is populated. */
async function waitForAccountsLoaded() {
  // The counter Select renders a placeholder only when accounts are loaded and
  // there are eligible options. Until then `counterAccountOptions` is empty
  // and the trigger shows "No eligible accounts" while disabled.
  await waitFor(() => {
    const trigger = screen.getByTestId('import-row-counter-0');
    expect(trigger).toHaveTextContent(/optional/i);
  });
}

describe('ImportPreview — counter-account picker', () => {
  it('renders the counter picker for a transfer-flagged row', async () => {
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    await waitForAccountsLoaded();
    expect(screen.getByTestId('import-row-counter-0')).toBeInTheDocument();
  });

  it('does NOT render the counter picker for a non-transfer row', async () => {
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    await waitForAccountsLoaded();
    // Row 1 is non-transfer; the cell renders empty (no Select trigger).
    expect(screen.queryByTestId('import-row-counter-1')).not.toBeInTheDocument();
  });

  it('flows a picked counter account into the commit payload', async () => {
    let captured: { transactions: Array<Record<string, unknown>> } | null = null;
    server.use(
      http.post('*/imports/commit', async ({ request }) => {
        captured = (await request.json()) as {
          transactions: Array<Record<string, unknown>>;
        };
        return HttpResponse.json(
          { importBatchId: 'batch-1', importedCount: 2, skippedDuplicates: 0 },
          { status: 201 },
        );
      }),
    );

    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    await waitForAccountsLoaded();

    await user.click(screen.getByTestId('import-row-counter-0'));
    // ING Savings is one of the eligible MDL counters seeded in the handler mocks.
    const ingOption = await screen.findByRole('option', { name: /ING Savings/i });
    await user.click(ingOption);

    await user.click(screen.getByTestId('import-commit-button'));

    await waitFor(() => {
      expect(captured).not.toBeNull();
    });
    // Narrow `captured` for the type checker — past the waitFor it is non-null.
    const body = captured as unknown as { transactions: Array<Record<string, unknown>> };
    expect(body.transactions).toHaveLength(2);
    expect(body.transactions[0]).toMatchObject({
      isTransfer: true,
      counterAccountId: ING_SAVINGS_ID,
    });
  });

  it('submits successfully when the counter is left blank (no toast, payload sends null)', async () => {
    let captured: { transactions: Array<Record<string, unknown>> } | null = null;
    server.use(
      http.post('*/imports/commit', async ({ request }) => {
        captured = (await request.json()) as {
          transactions: Array<Record<string, unknown>>;
        };
        return HttpResponse.json(
          { importBatchId: 'batch-2', importedCount: 2, skippedDuplicates: 0 },
          { status: 201 },
        );
      }),
    );

    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    await waitForAccountsLoaded();

    // Don't touch the counter picker for the transfer row — leave it blank.
    await user.click(screen.getByTestId('import-commit-button'));

    await waitFor(() => {
      expect(captured).not.toBeNull();
    });
    const body = captured as unknown as { transactions: Array<Record<string, unknown>> };
    expect(body.transactions).toHaveLength(2);
    // The spread serializes `counterAccountId: null` for transfer rows when blank.
    expect(body.transactions[0]).toMatchObject({
      isTransfer: true,
      counterAccountId: null,
    });
    // Non-transfer row must not carry transfer/counter keys.
    expect(body.transactions[1]).not.toHaveProperty('isTransfer');
    expect(body.transactions[1]).not.toHaveProperty('counterAccountId');

    // No error toast should have surfaced — counter is optional.
    expect(screen.queryByText(/counter account is required/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/select.*counter/i)).not.toBeInTheDocument();
  });

  it('clears the picked counter when the user toggles isTransfer off', async () => {
    const captures: Array<{ transactions: Array<Record<string, unknown>> }> = [];
    server.use(
      http.post('*/imports/commit', async ({ request }) => {
        captures.push((await request.json()) as { transactions: Array<Record<string, unknown>> });
        return HttpResponse.json(
          { importBatchId: `batch-${captures.length}`, importedCount: 2, skippedDuplicates: 0 },
          { status: 201 },
        );
      }),
    );

    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    await waitForAccountsLoaded();

    // 1. Pick a counter on the transfer row.
    await user.click(screen.getByTestId('import-row-counter-0'));
    const ingOption = await screen.findByRole('option', { name: /ING Savings/i });
    await user.click(ingOption);

    // 2. Now uncheck the transfer flag for that row. The reducer must wipe
    //    `counterAccountId` back to null even though the picker UI vanishes.
    const transferCell = screen.getByTestId('import-row-transfer-0');
    const transferCheckbox = within(transferCell.parentElement as HTMLElement).getByRole(
      'checkbox',
    ) as HTMLInputElement;
    await user.click(transferCheckbox);

    // 3. Re-check it so the picker comes back — it should now be empty (not
    //    remembering the prior choice).
    await user.click(transferCheckbox);

    await waitForAccountsLoaded();
    expect(screen.getByTestId('import-row-counter-0')).toHaveTextContent(/optional/i);

    // 4. Commit and verify the payload carries no counter.
    await user.click(screen.getByTestId('import-commit-button'));

    await waitFor(() => {
      expect(captures).toHaveLength(1);
    });
    const body = captures[0]!;
    expect(body.transactions[0]).toMatchObject({
      isTransfer: true,
      counterAccountId: null,
    });
  });
});

describe('ImportPreview — cross-currency counter', () => {
  it('reveals the received-amount field + rate and ships counterAmount on commit', async () => {
    let captured: { transactions: Array<Record<string, unknown>> } | null = null;
    server.use(
      // 500 MDL → ~29.10 USD for the row's date.
      http.get('*/fx-rates/convert', () =>
        HttpResponse.json({ convertedAmount: 29.1, rate: 0.0582, hasRate: true }),
      ),
      http.post('*/imports/commit', async ({ request }) => {
        captured = (await request.json()) as { transactions: Array<Record<string, unknown>> };
        return HttpResponse.json(
          { importBatchId: 'batch-fx', importedCount: 2, skippedDuplicates: 0 },
          { status: 201 },
        );
      }),
    );

    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    await waitForAccountsLoaded();

    // Pick the USD counter on the transfer row (row 0, 500 MDL).
    await user.click(screen.getByTestId('import-row-counter-0'));
    await user.click(await screen.findByRole('option', { name: /XTB/i }));

    // The received-amount field appears and pre-fills from the FX convert.
    const received = (await screen.findByTestId('import-row-counter-amount-0')) as HTMLInputElement;
    await waitFor(() => {
      expect(received.value).toBe('29.10');
    });

    // The rate line reflects sourceAmount / received = 500 / 29.1, MDL/USD.
    await waitFor(() => {
      expect(screen.getByTestId('import-row-rate-0')).toHaveTextContent(/MDL\/USD/);
    });

    await user.click(screen.getByTestId('import-commit-button'));

    await waitFor(() => {
      expect(captured).not.toBeNull();
    });
    const body = captured as unknown as { transactions: Array<Record<string, unknown>> };
    expect(body.transactions[0]).toMatchObject({
      isTransfer: true,
      counterAccountId: XTB_USD_ID,
      counterAmount: 29.1,
    });
  });

  it('blocks commit when a cross-currency received amount is missing', async () => {
    let posted = false;
    server.use(
      // No rate ⇒ the received-amount field stays blank.
      http.get('*/fx-rates/convert', () =>
        HttpResponse.json({ convertedAmount: null, rate: null, hasRate: false }),
      ),
      http.post('*/imports/commit', async () => {
        posted = true;
        return HttpResponse.json(
          { importBatchId: 'batch-block', importedCount: 2, skippedDuplicates: 0 },
          { status: 201 },
        );
      }),
    );

    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    await waitForAccountsLoaded();

    await user.click(screen.getByTestId('import-row-counter-0'));
    await user.click(await screen.findByRole('option', { name: /XTB/i }));

    const received = (await screen.findByTestId('import-row-counter-amount-0')) as HTMLInputElement;
    expect(received.value).toBe('');

    await user.click(screen.getByTestId('import-commit-button'));

    // The guard blocks the commit (a toast points the user at the row); no
    // POST /imports/commit fires. We assert the network outcome rather than the
    // toast text since this harness doesn't mount a <Toaster>.
    await new Promise((r) => setTimeout(r, 50));
    expect(posted).toBe(false);
    // The received-amount field is still present so the user can fix it.
    expect(screen.getByTestId('import-row-counter-amount-0')).toBeInTheDocument();
  });

  it('does not show the received-amount field for a same-currency counter', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    await waitForAccountsLoaded();

    // ING Savings is MDL, same as the import account → no received-amount field.
    await user.click(screen.getByTestId('import-row-counter-0'));
    await user.click(await screen.findByRole('option', { name: /ING Savings/i }));

    expect(screen.queryByTestId('import-row-counter-amount-0')).not.toBeInTheDocument();
  });
});

describe('ImportPreview — duplicate statement rows', () => {
  // A maib statement legitimately repeats rows with identical
  // date+direction+amount+description (e.g. two "ATM MAIB REC IALOVENI 2" of
  // 10000 on the same day), and the parser keeps these snapshot duplicates on
  // purpose. A content-based React key would collide across them, emitting
  // "Encountered two children with the same key" and risking per-row state
  // mis-association. Rows are therefore keyed by array index.
  function buildPreviewWithDuplicateRows(): StatementPreviewDto {
    const dupRow = {
      transactionDate: '2025-05-04',
      direction: 'Expense' as const,
      amount: 10000,
      description: 'ATM MAIB REC IALOVENI 2',
      isDuplicate: false,
      isTransfer: false,
    };
    return {
      fileHash: 'hash-dup',
      statementPeriod: { from: '2025-05-01', to: '2025-05-31' },
      bankSource: 'Maib',
      summary: {
        openingBalance: 25000,
        closingBalance: 5000,
        totalIn: 0,
        totalOut: 20000,
        totalFees: 0,
      },
      // Two byte-for-byte identical rows — a content key would collide.
      transactions: [{ ...dupRow }, { ...dupRow }],
    };
  }

  it('renders identical duplicate rows without a React duplicate-key warning', async () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

    try {
      renderWithClient(
        <ImportPreview
          preview={buildPreviewWithDuplicateRows()}
          accountId={IMPORT_ACCOUNT_ID}
          fileName="maib-may.pdf"
          onCancel={() => {}}
        />,
      );

      // Both duplicate rows must render. They're non-transfer, so there is no
      // counter picker to wait on — wait on the rows themselves instead.
      await waitFor(() => {
        expect(screen.getAllByText('ATM MAIB REC IALOVENI 2')).toHaveLength(2);
      });

      // No "same key" / "unique key" warning was emitted during render.
      const sameKeyWarning = errorSpy.mock.calls.find((call) =>
        call.some(
          (arg) =>
            typeof arg === 'string' && (/same key/i.test(arg) || /unique.*key/i.test(arg)),
        ),
      );
      expect(sameKeyWarning).toBeUndefined();
    } finally {
      errorSpy.mockRestore();
    }
  });
});

describe('ImportPreview — learn-with-confirm', () => {
  // Row 0 here is a plain expense the suggester missed; row 1 carries a
  // suggestion so we can prove the "already known" path stays quiet.
  function buildLearnPreview(): StatementPreviewDto {
    return {
      fileHash: 'hash-learn',
      statementPeriod: { from: '2025-05-01', to: '2025-05-31' },
      bankSource: 'Maib',
      summary: {
        openingBalance: 1000,
        closingBalance: 1200,
        totalIn: 500,
        totalOut: 300,
        totalFees: 0,
      },
      transactions: [
        {
          // No suggestion → categorizing it should propose a learn rule.
          transactionDate: '2025-05-04',
          direction: 'Expense',
          amount: 120,
          description: 'LINELLA SRL 1234*5678 04.05',
          isDuplicate: false,
          isTransfer: false,
        },
        {
          // The suggester already matched Dining out → no rule should be proposed
          // when the user keeps that exact suggestion.
          transactionDate: '2025-05-12',
          direction: 'Expense',
          amount: 35,
          description: 'TUCANO COFFEE',
          suggestedCategoryId: DINING_OUT_ID,
          suggestedCategoryName: 'Dining out',
          isDuplicate: false,
          isTransfer: false,
        },
      ],
    };
  }

  /** The category Select only lists real options once categories have loaded. */
  async function pickCategory(user: ReturnType<typeof userEvent.setup>, idx: number, name: RegExp) {
    await user.click(screen.getByTestId(`import-row-category-${idx}`));
    const option = await screen.findByRole('option', { name });
    await user.click(option);
  }

  function stubCommit(capture: { current: Record<string, unknown> | null }) {
    server.use(
      http.post('*/imports/commit', async ({ request }) => {
        capture.current = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          { importBatchId: 'batch-learn', importedCount: 2, skippedDuplicates: 0 },
          { status: 201 },
        );
      }),
    );
  }

  it('reveals a pre-filled learn keyword and ships it on commit when the user picks a non-suggested category', async () => {
    const capture: { current: Record<string, unknown> | null } = { current: null };
    stubCommit(capture);

    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildLearnPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    await pickCategory(user, 0, /Groceries/i);

    // The learn affordance appears with a keyword derived from the memo —
    // proposeKeyword keeps the first two meaningful tokens ("LINELLA SRL") and
    // drops the card mask + date that follow.
    const keywordInput = (await screen.findByTestId(
      'import-row-learn-keyword-0',
    )) as HTMLInputElement;
    expect(keywordInput.value).toBe('LINELLA SRL');

    await user.click(screen.getByTestId('import-commit-button'));

    await waitFor(() => {
      expect(capture.current).not.toBeNull();
    });
    const body = capture.current as { learnedPatterns?: Array<Record<string, unknown>> };
    expect(body.learnedPatterns).toEqual([{ keyword: 'LINELLA SRL', categoryId: GROCERIES_ID }]);
  });

  it('keeps a user-edited learn keyword when switching to another non-suggested category', async () => {
    const capture: { current: Record<string, unknown> | null } = { current: null };
    stubCommit(capture);
    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildLearnPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    await pickCategory(user, 0, /Groceries/i);
    const keywordInput = (await screen.findByTestId(
      'import-row-learn-keyword-0',
    )) as HTMLInputElement;
    // Edit the proposed keyword, then switch to another non-suggested category.
    await user.clear(keywordInput);
    await user.type(keywordInput, 'CUSTOM KW');
    await user.tab();
    await pickCategory(user, 0, /Transport/i);

    // The edited keyword survives the category switch (reducer reuse branch).
    await waitFor(() => {
      expect((screen.getByTestId('import-row-learn-keyword-0') as HTMLInputElement).value).toBe(
        'CUSTOM KW',
      );
    });

    // Switching back to Uncategorized disables learning (the !shouldLearn arm).
    await pickCategory(user, 0, /^Uncategorized$/i);
    await waitFor(() => {
      expect(screen.queryByTestId('import-row-learn-keyword-0')).not.toBeInTheDocument();
    });
  });

  it('omits learnedPatterns when the proposed keyword is dismissed', async () => {
    const capture: { current: Record<string, unknown> | null } = { current: null };
    stubCommit(capture);

    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildLearnPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    await pickCategory(user, 0, /Groceries/i);
    expect(await screen.findByTestId('import-row-learn-keyword-0')).toBeInTheDocument();

    // Opt out via the dismiss (×) control — the affordance disappears.
    await user.click(screen.getByTestId('import-row-learn-toggle-0'));
    expect(screen.queryByTestId('import-row-learn-keyword-0')).not.toBeInTheDocument();

    await user.click(screen.getByTestId('import-commit-button'));

    await waitFor(() => {
      expect(capture.current).not.toBeNull();
    });
    const body = capture.current as { learnedPatterns?: unknown };
    expect(body).not.toHaveProperty('learnedPatterns');
  });

  it('omits learnedPatterns when the keyword is cleared to blank', async () => {
    const capture: { current: Record<string, unknown> | null } = { current: null };
    stubCommit(capture);

    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildLearnPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    await pickCategory(user, 0, /Groceries/i);
    const keywordInput = (await screen.findByTestId(
      'import-row-learn-keyword-0',
    )) as HTMLInputElement;
    await user.clear(keywordInput);

    await user.click(screen.getByTestId('import-commit-button'));

    await waitFor(() => {
      expect(capture.current).not.toBeNull();
    });
    const body = capture.current as { learnedPatterns?: unknown };
    expect(body).not.toHaveProperty('learnedPatterns');
  });

  it('does NOT propose a rule when the user keeps the suggester’s own suggestion', async () => {
    const capture: { current: Record<string, unknown> | null } = { current: null };
    stubCommit(capture);

    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildLearnPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    // Row 1 is pre-set to Dining out (the suggestion). Re-selecting the same
    // category must not surface the learn affordance.
    await pickCategory(user, 1, /Dining out/i);
    expect(screen.queryByTestId('import-row-learn-keyword-1')).not.toBeInTheDocument();

    await user.click(screen.getByTestId('import-commit-button'));

    await waitFor(() => {
      expect(capture.current).not.toBeNull();
    });
    const body = capture.current as { learnedPatterns?: unknown };
    expect(body).not.toHaveProperty('learnedPatterns');
  });
});

describe('ImportPreview — inline category creation', () => {
  // Row 0 is an uncategorized expense; picking "+ New category…" should open
  // the shared create dialog seeded to the row's flow, create the category,
  // assign it back to the row, and ship its id on commit.
  function buildPlainExpensePreview(): StatementPreviewDto {
    return {
      fileHash: 'hash-inline',
      statementPeriod: { from: '2025-05-01', to: '2025-05-31' },
      bankSource: 'Maib',
      summary: {
        openingBalance: 1000,
        closingBalance: 880,
        totalIn: 0,
        totalOut: 120,
        totalFees: 0,
      },
      transactions: [
        {
          transactionDate: '2025-05-04',
          direction: 'Expense',
          amount: 120,
          description: 'BRICOSTORE CHISINAU',
          isDuplicate: false,
          isTransfer: false,
        },
      ],
    };
  }

  it('opens the create dialog from the row picker without setting the sentinel as the value', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildPlainExpensePreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    // Open the row's category picker and choose the "+ New category…" action.
    await user.click(screen.getByTestId('import-row-category-0'));
    await user.click(await screen.findByTestId('import-row-new-category-0'));

    // The shared create dialog opens…
    expect(await screen.findByTestId('create-category-dialog')).toBeInTheDocument();
    // …and the row's trigger still shows "Uncategorized", NOT the sentinel —
    // selecting the action must not commit a value to the row.
    expect(screen.getByTestId('import-row-category-0')).toHaveTextContent(/uncategorized/i);
  });

  it('defaults the new-category flow to the row direction and ships the new id on commit', async () => {
    let captured: { transactions: Array<Record<string, unknown>> } | null = null;
    server.use(
      // Return a fresh id for the create; the import flow assigns it to the row.
      http.post('*/categories', () =>
        HttpResponse.json({ id: 'new-inline-category-id' }, { status: 201 }),
      ),
      http.post('*/imports/commit', async ({ request }) => {
        captured = (await request.json()) as { transactions: Array<Record<string, unknown>> };
        return HttpResponse.json(
          { importBatchId: 'batch-inline', importedCount: 1, skippedDuplicates: 0 },
          { status: 201 },
        );
      }),
    );

    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildPlainExpensePreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    await user.click(screen.getByTestId('import-row-category-0'));
    await user.click(await screen.findByTestId('import-row-new-category-0'));

    const dialog = await screen.findByTestId('create-category-dialog');
    // The flow seeds from the row's direction (Expense) — its trigger reflects it.
    expect(within(dialog).getByTestId('category-flow-select')).toHaveTextContent('Expense');

    await user.type(within(dialog).getByTestId('category-name-input'), 'Hardware');
    await user.click(within(dialog).getByTestId('category-submit-button'));

    // Dialog closes after a successful create.
    await waitFor(() => {
      expect(screen.queryByTestId('create-category-dialog')).not.toBeInTheDocument();
    });

    // The row now carries the new category id; commit ships it.
    await user.click(screen.getByTestId('import-commit-button'));

    await waitFor(() => {
      expect(captured).not.toBeNull();
    });
    const body = captured as unknown as { transactions: Array<Record<string, unknown>> };
    expect(body.transactions[0]).toMatchObject({ categoryId: 'new-inline-category-id' });
  });
});

describe('ImportPreview — per-row note', () => {
  it('reveals a note input on demand, ships a typed note, and omits it for a blank row', async () => {
    let captured: { transactions: Array<Record<string, unknown>> } | null = null;
    server.use(
      http.post('*/imports/commit', async ({ request }) => {
        captured = (await request.json()) as { transactions: Array<Record<string, unknown>> };
        return HttpResponse.json(
          { importBatchId: 'batch-note', importedCount: 2, skippedDuplicates: 0 },
          { status: 201 },
        );
      }),
    );

    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    // The note input is hidden until the affordance is clicked (reveal-on-demand).
    expect(screen.queryByTestId('import-row-note-input-0')).not.toBeInTheDocument();

    // Reveal row 0's note editor and type a note. Leave row 1 untouched.
    await user.click(screen.getByTestId('import-row-note-toggle-0'));
    const noteInput = (await screen.findByTestId('import-row-note-input-0')) as HTMLTextAreaElement;
    await user.type(noteInput, '  Reimbursable expense  ');

    await user.click(screen.getByTestId('import-commit-button'));

    await waitFor(() => {
      expect(captured).not.toBeNull();
    });
    const body = captured as unknown as { transactions: Array<Record<string, unknown>> };
    // Row 0 ships the trimmed note; row 1 (never touched) omits the key entirely.
    expect(body.transactions[0]).toMatchObject({ notes: 'Reimbursable expense' });
    expect(body.transactions[1]).not.toHaveProperty('notes');
  });
});

describe('ImportPreview — summary reconciliation', () => {
  // maib books commissions INSIDE `totalOut`, and `closingBalance` is maib's
  // "Sold Disponibil" = the true balance = opening + in − out. Fees are NOT
  // subtracted again, so the statement ties on opening + in − out:
  // opening (1000) + in (500) − out (300) = closing (1200). The 125 of fees is
  // already part of the 300 Out (e.g. 175 of "real" spend + 125 commission).
  function buildReconcilingPreview(): StatementPreviewDto {
    return {
      fileHash: 'hash-fees',
      statementPeriod: { from: '2025-05-01', to: '2025-05-31' },
      bankSource: 'Maib',
      summary: {
        openingBalance: 1000,
        closingBalance: 1200,
        totalIn: 500,
        totalOut: 300,
        totalFees: 125,
      },
      transactions: [
        {
          transactionDate: '2025-05-04',
          direction: 'Expense',
          amount: 175,
          description: 'LINELLA SRL CHISINAU',
          isDuplicate: false,
          isTransfer: false,
        },
        {
          // A parser fee row — counted in Out and ALSO in rowFees (subset of Out).
          transactionDate: '2025-05-04',
          direction: 'Expense',
          amount: 125,
          description: 'Comision: card maintenance',
          isDuplicate: false,
          isTransfer: false,
        },
        {
          transactionDate: '2025-05-20',
          direction: 'Income',
          amount: 500,
          description: 'SALARY DEPOSIT',
          isDuplicate: false,
          isTransfer: false,
        },
      ],
    };
  }

  it('renders the Fees stat from summary.totalFees', () => {
    renderWithClient(
      <ImportPreview
        preview={buildReconcilingPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    expect(screen.getByTestId('import-summary-fees')).toHaveTextContent('125');
  });

  it('shows the ✓ matches-closing reconciliation line without subtracting fees', () => {
    renderWithClient(
      <ImportPreview
        preview={buildReconcilingPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    const line = screen.getByTestId('import-summary-reconciliation');
    // Identity is opening + in − out (fees are already inside Out, never
    // subtracted a second time).
    expect(line).toHaveTextContent(/Opening \+ In − Out =/);
    expect(line).not.toHaveTextContent(/− Fees/);
    expect(line).toHaveTextContent(/matches closing/i);
    expect(line).not.toHaveTextContent(/doesn't match/i);
  });

  it('shows a row-derived parse check that ties to the header and reports the resulting balance', () => {
    renderWithClient(
      <ImportPreview
        preview={buildReconcilingPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="maib-may.pdf"
        onCancel={() => {}}
      />,
    );

    const check = screen.getByTestId('import-rows-check');
    // rowIn=500, rowOut=300 (175 + 125), rowFees=125 (the "Comision:" row).
    // All three tie to the header → ✓, no ⚠ deltas.
    expect(check).toHaveTextContent(/From parsed rows:/);
    expect(check).not.toHaveTextContent(/⚠/);
    expect(check).toHaveTextContent(/Fees/);
    // Resulting account balance = opening (1000) + in (500) − out (300) = 1200.
    // MDL formats with a "." thousands separator → "1.200,00 MDL".
    expect(check).toHaveTextContent(/Opening \+ In − Out = 1\.200,00 MDL/);
  });
});
