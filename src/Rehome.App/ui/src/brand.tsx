import { useId } from "react";

/**
 * ReHome's mark: a house of app tiles, the last one dropping into place. The same shapes as assets/logo.svg, which
 * the .ico and the README use. Drawn inline so it stays sharp at any size and needs no file served.
 */
export function Logo({ size = 64, className }: { size?: number; className?: string }) {
  const id = useId();
  return (
    <svg className={className} width={size} height={size} viewBox="0 0 256 256" aria-hidden="true">
      <defs>
        <linearGradient id={`${id}bg`} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#8079f2" />
          <stop offset="1" stopColor="#4a42c6" />
        </linearGradient>
        <linearGradient id={`${id}sheen`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#fff" stopOpacity=".2" />
          <stop offset=".55" stopColor="#fff" stopOpacity="0" />
        </linearGradient>
      </defs>
      <rect width="256" height="256" rx="60" fill={`url(#${id}bg)`} />
      <rect width="256" height="256" rx="60" fill={`url(#${id}sheen)`} />
      <path
        d="M128 46 L210 116 H190 V194 a16 16 0 0 1 -16 16 H82 a16 16 0 0 1 -16 -16 V116 H46 Z"
        fill="#fff"
        stroke="#fff"
        strokeWidth="14"
        strokeLinejoin="round"
      />
      <g fill="#5850d8">
        <rect x="94" y="122" width="31" height="31" rx="8" />
        <rect x="94" y="159" width="31" height="31" rx="8" />
        <rect x="131" y="159" width="31" height="31" rx="8" />
        <rect x="131" y="122" width="31" height="31" rx="8" opacity=".38" />
        <rect className="logo-arriving" x="139" y="108" width="31" height="31" rx="8" transform="rotate(12 154.5 123.5)" />
      </g>
    </svg>
  );
}
