'use client';

import Link from 'next/link';
import { useState } from 'react';
import { PageHeader } from '@/src/components/page-header';
import { ImportPreview } from '@/src/components/transactions/import-preview';
import { ImportUpload } from '@/src/components/transactions/import-upload';
import type { StatementPreviewDto } from '@/src/types/api';

type Step = 'upload' | 'preview';

export default function ImportPage() {
  const [step, setStep] = useState<Step>('upload');
  const [accountId, setAccountId] = useState<string>('');
  const [fileName, setFileName] = useState<string>('');
  const [preview, setPreview] = useState<StatementPreviewDto | null>(null);

  return (
    <>
      <PageHeader
        title="Import statement"
        description="Upload a maib PDF and review parsed transactions before committing."
      />
      <div className="mb-4 text-sm text-muted-foreground">
        <Link className="text-primary hover:underline" href="/transactions">
          ← Back to transactions
        </Link>
      </div>

      {step === 'upload' && (
        <ImportUpload
          onParsed={(p, acc, name) => {
            setPreview(p);
            setAccountId(acc);
            setFileName(name);
            setStep('preview');
          }}
        />
      )}

      {step === 'preview' && preview && (
        <ImportPreview
          preview={preview}
          accountId={accountId}
          fileName={fileName}
          onCancel={() => setStep('upload')}
        />
      )}
    </>
  );
}
