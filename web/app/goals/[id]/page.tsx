import { GoalDetailView } from '@/src/components/goals/detail/goal-detail-view';

interface Props {
  // Next.js 15 turns `params` into a Promise — we await it before passing
  // the id down to the Client Component child.
  params: Promise<{ id: string }>;
}

export default async function GoalDetailPage({ params }: Props) {
  const { id } = await params;
  return <GoalDetailView id={id} />;
}
