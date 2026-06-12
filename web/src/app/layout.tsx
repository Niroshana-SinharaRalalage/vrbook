import type { Metadata, Viewport } from 'next';
import { Inter } from 'next/font/google';

import { Providers } from '@/components/Providers';
import { DevPersonaSwitcher } from '@/components/DevPersonaSwitcher';
import './globals.css';

const inter = Inter({
  subsets: ['latin'],
  variable: '--font-sans',
  display: 'swap',
});

export const metadata: Metadata = {
  metadataBase: new URL(process.env.NEXT_PUBLIC_SITE_URL ?? 'https://www.vrbook.example.com'),
  title: {
    default: 'VrBook — Direct vacation rental bookings',
    template: '%s · VrBook',
  },
  description:
    'Book direct with the host. No service fee. Verified properties, real reviews, and instant messaging.',
  applicationName: 'VrBook',
  openGraph: {
    type: 'website',
    siteName: 'VrBook',
  },
  twitter: {
    card: 'summary_large_image',
  },
};

export const viewport: Viewport = {
  themeColor: [
    { media: '(prefers-color-scheme: light)', color: '#fff7ed' },
    { media: '(prefers-color-scheme: dark)', color: '#3f0a13' },
  ],
};

interface RootLayoutProps {
  readonly children: React.ReactNode;
}

const RootLayout = ({ children }: RootLayoutProps) => {
  return (
    <html lang="en" className={inter.variable} suppressHydrationWarning>
      <body>
        <Providers>{children}</Providers>
        <DevPersonaSwitcher />
      </body>
    </html>
  );
};

export default RootLayout;
