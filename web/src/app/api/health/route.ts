import { NextResponse } from 'next/server';

// Container Apps / Kubernetes probe target. Keep this synchronous and cheap —
// it must not depend on downstream services or it stops being a self-health check.

export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

export const GET = () => {
  return NextResponse.json({
    status: 'ok',
    service: 'vrbook-web',
    timestamp: new Date().toISOString(),
  });
};
