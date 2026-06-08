'use client';

import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { ImportDataResponse } from '@/src/types/api';
import { accountKeys } from './accounts';
import { budgetKeys } from './budgets';
import { categoryKeys } from './categories';
import { apiClient } from './client';
import { dashboardKeys } from './dashboard';
import { fxRateKeys } from './fx-rates';
import { goalKeys } from './goals';
import { reportsKeys } from './reports';
import { transactionKeys } from './transactions';

const DEFAULT_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? '';

/**
 * Builds the absolute URL the browser hits to download a full JSON backup.
 * Exported for testing; real callers use {@link downloadBackup}.
 */
export function getBackupDownloadUrl(baseUrl: string = DEFAULT_BASE_URL): string {
  return `${baseUrl}/data/export`;
}

/**
 * Triggers a browser download of the full JSON backup by synthesizing a
 * hidden `<a download>` and clicking it — the same technique as
 * `ExportCsvButton`. No fetch, no parsing: the backend streams the file
 * with a `Content-Disposition: attachment` header, so the browser handles
 * the save dialog and the user stays on the Data page.
 */
export function downloadBackup(baseUrl: string = DEFAULT_BASE_URL): void {
  if (typeof document === 'undefined') return;
  const anchor = document.createElement('a');
  anchor.href = getBackupDownloadUrl(baseUrl);
  anchor.rel = 'noopener';
  // The backend's Content-Disposition filename wins when present; this is
  // only a sane default for the rare case it's absent.
  anchor.download = 'money-management-backup.json';
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
}

/**
 * POST /data/import — multipart upload of a `.json` backup under the `file`
 * field. A successful restore REPLACES ALL data server-side, so on success
 * we blow away every query root in the app. We reuse each slice's exported
 * key constant where one exists (`accountKeys.all`, etc.) and fall back to
 * a literal root array otherwise, so this stays in sync as those roots
 * evolve.
 */
export function useImportData() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (file: File) => {
      const formData = new FormData();
      formData.append('file', file);
      return apiClient.postForm<ImportDataResponse>('/data/import', formData);
    },
    onSuccess: () => {
      for (const queryKey of [
        accountKeys.all,
        transactionKeys.all,
        categoryKeys.all,
        budgetKeys.all,
        goalKeys.all,
        fxRateKeys.all,
        dashboardKeys.all,
        reportsKeys.all,
      ]) {
        queryClient.invalidateQueries({ queryKey });
      }
    },
  });
}
