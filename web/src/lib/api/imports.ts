'use client';

import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { CommitImportRequest, CommitResultDto, StatementPreviewDto } from '@/src/types/api';
import { apiClient } from './client';

export interface ParseStatementInput {
  file: File;
  accountId: string;
}

export function useParseStatement() {
  return useMutation({
    mutationFn: ({ file, accountId }: ParseStatementInput) => {
      const formData = new FormData();
      formData.append('file', file);
      formData.append('accountId', accountId);
      return apiClient.postForm<StatementPreviewDto>('/imports/parse', formData);
    },
  });
}

export function useCommitImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CommitImportRequest) =>
      apiClient.post<CommitResultDto>('/imports/commit', input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['transactions'] });
      // Imported rows move account balances + the per-account detail/Performance.
      queryClient.invalidateQueries({ queryKey: ['accounts'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      // Balance-over-time + monthly summaries are reports-rooted.
      queryClient.invalidateQueries({ queryKey: ['reports'] });
      // Imported expense rows may shift budgeted-category spend totals.
      queryClient.invalidateQueries({ queryKey: ['budgets'] });
      // Imported rows feed account balances → linked-mode goals' `saved`.
      queryClient.invalidateQueries({ queryKey: ['goals'] });
    },
  });
}
