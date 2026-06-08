import { ArrowLeft } from 'lucide-react';
import Link from 'next/link';
import { Button } from '@/src/components/ui/button';

interface Props {
  /** Distinguishes "Goal not found." from the generic fetch-error copy. */
  notFound?: boolean;
}

/**
 * Renders the empty-state for the detail page when the backend returns 404
 * (`savings_goal.not_found` problem details) or any other failure. The
 * back-link to `/goals` is the primary remediation in both cases.
 */
export function GoalDetailError({ notFound = false }: Props) {
  const heading = notFound ? 'Goal not found.' : 'Failed to load goal.';
  const subtext = notFound
    ? "The goal you're looking for doesn't exist or has been removed."
    : 'Something went wrong while loading this goal. Try again in a moment.';

  return (
    <div
      className="flex flex-col items-start gap-4 rounded-lg border bg-card p-8"
      role="alert"
      data-testid="goal-detail-error"
      data-not-found={notFound ? 'true' : 'false'}
    >
      <div className="space-y-1">
        <h2 className="text-lg font-semibold">{heading}</h2>
        <p className="text-sm text-muted-foreground">{subtext}</p>
      </div>
      <Button asChild variant="outline">
        <Link href="/goals" data-testid="goal-detail-error-back">
          <ArrowLeft className="h-4 w-4" />
          Back to goals
        </Link>
      </Button>
    </div>
  );
}
