import { ArrowLeft } from 'lucide-react';
import Link from 'next/link';
import { Button } from '@/src/components/ui/button';

interface Props {
  /** Distinguishes "Pool not found." from the generic fetch-error copy. */
  notFound?: boolean;
}

/**
 * Empty state for the detail page when the backend returns 404 or any other
 * failure. The back-link to `/pools` is the remediation in both cases.
 * Mirrors `LoanDetailError`.
 */
export function PoolDetailError({ notFound = false }: Props) {
  const heading = notFound ? 'Pool not found.' : 'Failed to load pool.';
  const subtext = notFound
    ? "The pool you're looking for doesn't exist or has been removed."
    : 'Something went wrong while loading this pool. Try again in a moment.';

  return (
    <div
      className="flex flex-col items-start gap-4 rounded-lg border bg-card p-8"
      role="alert"
      data-testid="pool-detail-error"
      data-not-found={notFound ? 'true' : 'false'}
    >
      <div className="space-y-1">
        <h2 className="text-lg font-semibold">{heading}</h2>
        <p className="text-sm text-muted-foreground">{subtext}</p>
      </div>
      <Button asChild variant="outline">
        <Link href="/pools" data-testid="pool-detail-error-back">
          <ArrowLeft className="h-4 w-4" />
          Back to pools
        </Link>
      </Button>
    </div>
  );
}
