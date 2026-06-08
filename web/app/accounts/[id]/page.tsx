import { AccountDetailView } from '@/src/components/accounts/detail/account-detail-view';

interface Props {
  // Next.js 15 turns `params` into a Promise — we await it before passing
  // the id down to the Client Component child.
  params: Promise<{ id: string }>;
}

export default async function AccountDetailPage({ params }: Props) {
  const { id } = await params;
  return <AccountDetailView id={id} />;
}
