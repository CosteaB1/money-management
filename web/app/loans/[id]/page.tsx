import { LoanDetailView } from '@/src/components/loans/detail/loan-detail-view';

interface Props {
  // Next.js 15 turns `params` into a Promise — we await it before passing
  // the id down to the Client Component child.
  params: Promise<{ id: string }>;
}

export default async function LoanDetailPage({ params }: Props) {
  const { id } = await params;
  return <LoanDetailView id={id} />;
}
