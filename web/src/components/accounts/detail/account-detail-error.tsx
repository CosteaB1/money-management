import { ArrowLeft } from 'lucide-react';
import Link from 'next/link';
import { Button } from '@/src/components/ui/button';

interface Props {
  /** Distinguishes "Account not found." from the generic fetch-error copy. */
  notFound?: boolean;
}

/**
 * Renders the empty-state for the detail page when the backend returns
 * 404 (`accounts.not_found` problem details) or any other failure. The
 * back-link to `/accounts` is the primary remediation in both cases.
 */
export function AccountDetailError({ notFound = false }: Props) {
  const heading = notFound ? 'Account not found.' : 'Failed to load account.';
  const subtext = notFound
    ? "The account you're looking for doesn't exist or has been removed."
    : 'Something went wrong while loading this account. Try again in a moment.';

  return (
    <div
      className="flex flex-col items-start gap-4 rounded-lg border bg-card p-8"
      role="alert"
      data-testid="account-detail-error"
      data-not-found={notFound ? 'true' : 'false'}
    >
      <div className="space-y-1">
        <h2 className="text-lg font-semibold">{heading}</h2>
        <p className="text-sm text-muted-foreground">{subtext}</p>
      </div>
      <Button asChild variant="outline">
        <Link href="/accounts" data-testid="account-detail-error-back">
          <ArrowLeft className="h-4 w-4" />
          Back to accounts
        </Link>
      </Button>
    </div>
  );
}
