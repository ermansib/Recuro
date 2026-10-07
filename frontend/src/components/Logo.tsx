import { product } from '../config/tenant'

export function Logo() {
  return (
    <>
      <svg width="34" height="34" viewBox="0 0 96 96" role="img" aria-label={product.name}>
        <defs>
          <linearGradient id="rcG" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="#818CF8" />
            <stop offset=".55" stopColor="#A78BFA" />
            <stop offset="1" stopColor="#22D3EE" />
          </linearGradient>
        </defs>
        <rect width="96" height="96" rx="24" fill="url(#rcG)" />
        <g stroke="#141C42" strokeWidth="9.5" strokeLinecap="round" strokeLinejoin="round" fill="none">
          <path d="M33 28 V70" />
          <path d="M33 28 H49 a13 13 0 0 1 0 26 H33" />
          <path d="M50 54 L66 70 M57.5 70 H66 V61.5" />
        </g>
      </svg>
      <div style={{ lineHeight: 1 }}>
        <div style={{ fontSize: 21, fontWeight: 800, letterSpacing: '-.8px', color: '#fff' }}>
          Recu
          <span style={{ background: 'linear-gradient(90deg,#A78BFA,#22D3EE)', WebkitBackgroundClip: 'text', backgroundClip: 'text', color: 'transparent' }}>ro</span>
        </div>
        <div style={{ fontSize: 7, letterSpacing: '2.4px', color: '#8FA3BF', marginTop: 2 }}>{product.tagline}</div>
      </div>
    </>
  )
}
