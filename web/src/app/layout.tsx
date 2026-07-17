import type { Metadata, Viewport } from 'next';
import { Inter } from 'next/font/google';
import { Suspense } from 'react';

import { Providers } from '@/components/Providers';
import { ConsentProvider } from '@/lib/consent/ConsentProvider';
import { CookieConsent } from '@/components/consent/CookieConsent';
import { AnalyticsRouteTracker } from '@/components/consent/AnalyticsRouteTracker';
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
        {/* VRB-311 — ConsentProvider wraps everything (incl. the MSAL-gated
            Providers) so the banner shows pre-auth; analytics stays consent-gated. */}
        <ConsentProvider>
          <Providers>{children}</Providers>
          <CookieConsent />
          <Suspense fallback={null}>
            <AnalyticsRouteTracker />
          </Suspense>
        </ConsentProvider>
      </body>
    </html>
  );
};

export default RootLayout;
