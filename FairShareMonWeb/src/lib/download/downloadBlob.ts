import type { BlobResult } from "@/lib/api/client";

/**
 * Triggers a browser download for a binary API response (CSV export, and later
 * the QR PNG / event export). Creates an object URL, clicks a hidden `<a download>`,
 * then revokes the URL. Uses the server-provided `filename` when present, falling
 * back to the supplied default so a download always has a sensible name.
 *
 * Kept as one shared helper (reused by M5 event export + M7 QR) so the blob →
 * download plumbing lives in exactly one place.
 */
export function downloadBlob(result: BlobResult, fallbackName: string): void {
  const url = URL.createObjectURL(result.blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = result.filename ?? fallbackName;
  anchor.rel = "noopener";
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  // Revoke on the next tick so the click has committed the navigation.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
