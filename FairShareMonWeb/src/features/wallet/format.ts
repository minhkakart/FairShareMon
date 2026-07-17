/**
 * Presentation-only helpers for the account number (OQ5a). Pure string display —
 * never numeric math on the account number (it is an identifier, not a quantity).
 */

/** Masked form: last 4 digits behind a fixed dot run (e.g. `•••• 1234`). */
export function maskAccount(accountNumber: string): string {
  return `•••• ${accountNumber.slice(-4)}`;
}

/** Grouped full form for on-demand reveal (e.g. `0071 0012 3456 7`). */
export function groupAccount(accountNumber: string): string {
  return accountNumber.replace(/(.{4})(?=.)/g, "$1 ");
}
