import { ImageResponse } from 'next/og';

export const runtime = 'edge';
export const alt = 'VrBook property';
export const size = { width: 1200, height: 630 };
export const contentType = 'image/png';

interface OgProps {
  readonly params: { slug: string };
}

// Dynamic OG image. F1 replaces with property cover + branded overlay.
const OgImage = ({ params }: OgProps) => {
  return new ImageResponse(
    (
      <div
        style={{
          width: '100%',
          height: '100%',
          display: 'flex',
          flexDirection: 'column',
          background: 'linear-gradient(135deg, #8b1c2b 0%, #ea580c 100%)',
          color: 'white',
          padding: 80,
          justifyContent: 'space-between',
          fontFamily: 'system-ui, sans-serif',
        }}
      >
        <div style={{ fontSize: 28, opacity: 0.85 }}>VrBook</div>
        <div style={{ fontSize: 64, fontWeight: 700, lineHeight: 1.1 }}>
          {params.slug.replace(/-/g, ' ')}
        </div>
        <div style={{ fontSize: 24, opacity: 0.85 }}>Book direct. No service fee.</div>
      </div>
    ),
    { ...size },
  );
};

export default OgImage;
