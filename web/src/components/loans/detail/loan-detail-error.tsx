import { ArrowLeft } from 'lucide-react';
import Link from 'next/link';
import { Button } from '@/src/components/ui/button';

interface Props {
  /** Distinguishes "Loan not found." from the generic fetch-error copy. */
  notFound?: boolean;
}

/**
 * Renders the empty-state for the detail page when the backend returns
 * 404 or any other failure. The back-link to `/loans` is the primary
 * remediation in both cases. Mirrors the goal-detail-error pattern.
 */
export function LoanDetailError({ notFound = false }: Props) {
  const heading = notFound ? 'Loan not found.' : 'Failed to load loan.';
  const subtext = notFound
    ? "The loan you're looking for doesn't exist or has been removed."
    : 'Something went wrong while loading this loan. Try again in a moment.';

  return (
    <div
      className="flex flex-col items-start gap-4 rounded-lg border bg-card p-8"
      role="alert"
      data-testid="loan-detail-error"
      data-not-found={notFound ? 'true' : 'false'}
    >
      <div className="space-y-1">
        <h2 className="text-lg font-semibold">{heading}</h2>
        <p className="text-sm text-muted-foreground">{subtext}</p>
      </div>
      <Button asChild variant="outline">
        <Link href="/loans" data-testid="loan-detail-error-back">
          <ArrowLeft className="h-4 w-4" />
          Back to loans
        </Link>
      </Button>
    </div>
  );
}
