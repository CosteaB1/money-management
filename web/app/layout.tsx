import type { Metadata } from 'next';
import { Header } from '@/src/components/layout/header';
import { Sidebar } from '@/src/components/layout/sidebar';
import './globals.css';
import { Providers } from './providers';

export const metadata: Metadata = {
  title: 'Money Management',
  description: 'Personal finance — tracking, budgets, goals.',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" suppressHydrationWarning>
      <body className="min-h-screen bg-background font-sans antialiased">
        <Providers>
          <div className="flex min-h-screen">
            <Sidebar />
            <div className="flex flex-1 flex-col">
              <Header />
              <main className="flex-1 p-4 md:p-8">{children}</main>
            </div>
          </div>
        </Providers>
      </body>
    </html>
  );
}
