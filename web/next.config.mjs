/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  output: 'standalone',
  poweredByHeader: false,
  // typedRoutes is opt-in and currently fights with our dynamic href usage
  // (search params, computed paths). Re-enable per-page later when stable.
  experimental: {
    typedRoutes: false,
  },
  images: {
    remotePatterns: [
      {
        // Replace at deploy time with the real Blob Storage account hostname,
        // e.g. stvrbookprod.blob.core.windows.net (proposal §23.3).
        protocol: 'https',
        hostname: '*.blob.core.windows.net',
        pathname: '/**',
      },
      {
        protocol: 'https',
        hostname: 'stvrbookprod.blob.core.windows.net',
        pathname: '/**',
      },
    ],
  },
  modularizeImports: {
    'lucide-react': {
      transform: 'lucide-react/dist/esm/icons/{{kebabCase member}}',
      preventFullImport: true,
    },
  },
};

export default nextConfig;
