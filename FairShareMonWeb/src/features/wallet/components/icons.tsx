/** Feature-local glyphs for the wallet + QR UI (decorative — labels carry meaning). */

export const QrIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true" width="1em" height="1em">
    <rect x="3" y="3" width="5" height="5" rx="1" />
    <rect x="12" y="3" width="5" height="5" rx="1" />
    <rect x="3" y="12" width="5" height="5" rx="1" />
    <path d="M12 12h2v2M16 12v5M12 16h2" strokeLinecap="round" />
  </svg>
);

export const DownloadIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="1em" height="1em">
    <path d="M10 3v9m0 0l-3.2-3.2M10 12l3.2-3.2M4 15.5h12" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

export const CopyIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="1em" height="1em">
    <rect x="7" y="7" width="9" height="9" rx="1.5" />
    <path d="M4 13V5.5A1.5 1.5 0 015.5 4H13" strokeLinecap="round" />
  </svg>
);

export const CheckIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="2.2" aria-hidden="true" width="1em" height="1em">
    <path d="M4 10l4 4 8-9" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

export const EyeIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true" width="1rem" height="1rem">
    <path d="M1.8 10S4.9 4.5 10 4.5 18.2 10 18.2 10 15.1 15.5 10 15.5 1.8 10 1.8 10z" strokeLinejoin="round" />
    <circle cx="10" cy="10" r="2.4" />
  </svg>
);

export const EyeOffIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true" width="1rem" height="1rem">
    <path d="M2.5 3.5l14 13M8 5c.65-.15 1.32-.23 2-.23 5.1 0 8.2 5.23 8.2 5.23a13 13 0 01-2.2 2.66M5.3 6.3A13 13 0 001.8 10s3.1 5.5 8.2 5.5c.9 0 1.75-.17 2.55-.45" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

export const StarIcon = () => (
  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true" width="1em" height="1em">
    <path d="M10 2l2.4 5 5.6.6-4.2 3.8 1.2 5.6L10 14.8 5 17l1.2-5.6L2 7.6 7.6 7z" />
  </svg>
);

export const StarOutlineIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true" width="1em" height="1em">
    <path d="M10 2.6l2.15 4.5 4.95.55-3.7 3.35 1.05 4.9L10 13.9 5.5 16.3l1.05-4.9-3.7-3.35 4.95-.55z" strokeLinejoin="round" />
  </svg>
);

export const PlusIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" width="1em" height="1em">
    <path d="M10 4v12M4 10h12" strokeLinecap="round" />
  </svg>
);

export const WalletIcon = () => (
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" width="28" height="28">
    <path d="M4 6a2 2 0 012-2h11a1 1 0 010 2H6v12h13v-3h-4a2 2 0 01-2-2v-2a2 2 0 012-2h4a2 2 0 012 2v7a2 2 0 01-2 2H6a2 2 0 01-2-2V6zm12 6v2h4v-2h-4z" />
  </svg>
);

export const TrashIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true" width="1em" height="1em">
    <path d="M4 6h12M8 6V4h4v2M6 6l.7 10h6.6L14 6" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

export const PencilIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true" width="1em" height="1em">
    <path d="M13.5 3.5l3 3L7 16H4v-3l9.5-9.5z" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

/* Maximize / expand — four outward corner arrows. Used by the enlarge badge + surface. */
export const ExpandIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="20" height="20">
    <path d="M12 3h5v5M8 17H3v-5M17 3l-6 6M3 17l6-6" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

/* Magnifier with a plus — zoom in. */
export const ZoomInIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="20" height="20">
    <circle cx="8.5" cy="8.5" r="5" />
    <path d="M12.2 12.2L17 17M6.5 8.5h4M8.5 6.5v4" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

/* Magnifier with a minus — zoom out. */
export const ZoomOutIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="20" height="20">
    <circle cx="8.5" cy="8.5" r="5" />
    <path d="M12.2 12.2L17 17M6.5 8.5h4" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

/* Reset-to-fit — a frame with inward corner arrows. */
export const FitIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="20" height="20">
    <path d="M4 8V4h4M16 8V4h-4M4 12v4h4M16 12v4h-4M8 8l-3-3M12 8l3-3M8 12l-3 3M12 12l3 3" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
