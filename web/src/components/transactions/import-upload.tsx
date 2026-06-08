'use client';

import { AlertCircle, FileText } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Button } from '@/src/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/src/components/ui/card';
import { Input } from '@/src/components/ui/input';
import { Label } from '@/src/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/src/components/ui/select';
import { useAccounts } from '@/src/lib/api/accounts';
import { useParseStatement } from '@/src/lib/api/imports';
import type { StatementPreviewDto } from '@/src/types/api';

const MAX_FILE_BYTES = 5 * 1024 * 1024;

interface Props {
  onParsed: (preview: StatementPreviewDto, accountId: string, fileName: string) => void;
}

export function ImportUpload({ onParsed }: Props) {
  const accountsQuery = useAccounts(false);
  const parse = useParseStatement();
  const [accountId, setAccountId] = useState<string>('');
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (accountId || !accountsQuery.data || accountsQuery.data.length === 0) return;
    const main = accountsQuery.data.find((a) => /main/i.test(a.name));
    setAccountId(main?.id ?? accountsQuery.data[0]?.id ?? '');
  }, [accountsQuery.data, accountId]);

  const handleFile = (next: File | null) => {
    setError(null);
    if (!next) {
      setFile(null);
      return;
    }
    if (next.type !== 'application/pdf' && !next.name.toLowerCase().endsWith('.pdf')) {
      setError('Please upload a PDF file.');
      return;
    }
    if (next.size > MAX_FILE_BYTES) {
      setError('File must be 5 MB or smaller.');
      return;
    }
    setFile(next);
  };

  const handleSubmit = async () => {
    setError(null);
    // Defensive: the Upload & preview button is disabled until BOTH an account
    // and a file are present, so these guards can't fire through the UI.
    /* v8 ignore start */
    if (!accountId) {
      setError('Please pick an account.');
      return;
    }
    if (!file) {
      setError('Please pick a PDF file.');
      return;
    }
    /* v8 ignore stop */
    try {
      const preview = await parse.mutateAsync({ file, accountId });
      onParsed(preview, accountId, file.name);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to parse the statement.');
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>Upload bank statement</CardTitle>
        <CardDescription>maib PDF statements only. Maximum 5 MB.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="import-account">Target account</Label>
          <Select value={accountId} onValueChange={setAccountId}>
            <SelectTrigger id="import-account" data-testid="import-account-select">
              <SelectValue placeholder="Select account" />
            </SelectTrigger>
            <SelectContent>
              {accountsQuery.data?.map((a) => (
                <SelectItem key={a.id} value={a.id}>
                  {a.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="space-y-2">
          <Label htmlFor="import-file">PDF statement</Label>
          <Input
            id="import-file"
            data-testid="import-file-input"
            type="file"
            accept="application/pdf,.pdf"
            onChange={(e) => handleFile(e.target.files?.[0] ?? null)}
          />
          {file && (
            <p className="flex items-center gap-2 text-sm text-muted-foreground">
              <FileText className="h-4 w-4" aria-hidden />
              <span>{file.name}</span>
              <span className="text-xs">({Math.round(file.size / 1024)} KB)</span>
            </p>
          )}
        </div>

        {error && (
          <div
            role="alert"
            data-testid="import-error"
            className="flex items-start gap-2 rounded-md border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive"
          >
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
            <span>{error}</span>
          </div>
        )}

        <div className="flex justify-end">
          <Button
            type="button"
            disabled={parse.isPending || !file || !accountId}
            onClick={handleSubmit}
            data-testid="import-parse-button"
          >
            {parse.isPending ? 'Parsing...' : 'Upload & preview'}
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
