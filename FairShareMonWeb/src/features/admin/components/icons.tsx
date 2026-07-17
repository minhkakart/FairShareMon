/**
 * Local decorative glyphs for the admin surfaces (copy always carries the
 * meaning; icons reinforce, never stand alone). Ported from the ui-designer's
 * M8 showcase. All `aria-hidden`.
 */

export const ShieldIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="1em" height="1em">
    <path d="M10 2.5l6 2.2v4.6c0 3.7-2.5 6.4-6 8.2-3.5-1.8-6-4.5-6-8.2V4.7l6-2.2z" strokeLinejoin="round" />
    <path d="M7.3 10l1.9 1.9L13 8" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

export const CheckIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="2.5" aria-hidden="true" width="1em" height="1em">
    <path d="M4 10l4 4 8-9" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

export const BanIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true" width="1em" height="1em">
    <circle cx="10" cy="10" r="7" />
    <path d="M5 5l10 10" strokeLinecap="round" />
  </svg>
);

export const CopyIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="1em" height="1em">
    <rect x="7" y="7" width="9" height="9" rx="1.5" />
    <path d="M4 13V4.5A1.5 1.5 0 015.5 3H13" strokeLinecap="round" />
  </svg>
);

export const RefreshIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="1em" height="1em">
    <path d="M16 4v4h-4M4 16v-4h4" strokeLinecap="round" strokeLinejoin="round" />
    <path d="M15.5 8a6 6 0 00-10.9-1.5M4.5 12a6 6 0 0010.9 1.5" strokeLinecap="round" />
  </svg>
);

export const WarnIcon = () => (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" width="1em" height="1em">
    <path d="M12 3.2 22 20H2L12 3.2Z" strokeLinejoin="round" strokeLinecap="round" />
    <path d="M12 9.5v4.5" strokeLinecap="round" />
    <circle cx="12" cy="17" r="0.6" fill="currentColor" stroke="none" />
  </svg>
);

export const UsersIcon = () => (
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" width="28" height="28">
    <path d="M9 11a4 4 0 100-8 4 4 0 000 8zm7 0a3 3 0 100-6 3 3 0 000 6zM2 20a7 7 0 0114 0v1H2v-1zm15.5 1v-1a8.5 8.5 0 00-1.7-5.1A5 5 0 0122 20v1h-4.5z" />
  </svg>
);

/** Ascending/descending sort chevrons for sortable table headers. */
export const SortAscIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" width="1em" height="1em">
    <path d="M10 15V5M6 9l4-4 4 4" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
export const SortDescIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" width="1em" height="1em">
    <path d="M10 5v10M6 11l4 4 4-4" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
export const SortNoneIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true" width="1em" height="1em">
    <path d="M7 8l3-3 3 3M7 12l3 3 3-3" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
