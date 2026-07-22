import { downloadBlob } from "@/lib/download/downloadBlob";
import type { MemberQrResponse } from "./api/types";

/**
 * Per-member QR share/download helpers, shared by the QR dialog footer AND the
 * lightbox's Download/Share plugin functions (OQ1a). Each member's QR arrives as
 * a `data:image/png;base64,<…>` data URL; these turn it into a `Blob`/`File` for
 * the download anchor and the Web Share API. No object-URL lifecycle to own here —
 * `downloadBlob` creates and revokes its own transient URL.
 */

/** Decode a `data:…;base64,<…>` data URL into an `image/png` `Blob`. */
export function dataUrlToBlob(dataUrl: string): Blob {
  const commaIndex = dataUrl.indexOf(",");
  const base64 = commaIndex >= 0 ? dataUrl.slice(commaIndex + 1) : dataUrl;
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) {
    bytes[i] = binary.charCodeAt(i);
  }
  return new Blob([bytes], { type: "image/png" });
}

/**
 * Build the download filename `qr-{memberName}.png` (OQ3a): keep spaces +
 * diacritics, replace only path-illegal characters (`/ \ : * ? " < > |` and
 * control chars) with `-`.
 */
export function qrFileName(memberName: string): string {
  let safe = "";
  for (const char of memberName) {
    const code = char.codePointAt(0) ?? 0;
    // Path-illegal punctuation OR any C0 control char → "-"; keep everything
    // else (spaces + diacritics survive, per OQ3a).
    safe += code < 0x20 || '\\/:*?"<>|'.includes(char) ? "-" : char;
  }
  safe = safe.trim();
  return `qr-${safe || "member"}.png`;
}

/** Build the shareable `File` for a member's QR (`qr-{memberName}.png`). */
function memberQrFile(member: MemberQrResponse): File {
  return new File([dataUrlToBlob(member.image)], qrFileName(member.memberName), {
    type: "image/png",
  });
}

/** Download a member's QR PNG as `qr-{memberName}.png` (reuses `downloadBlob`). */
export function downloadMemberQr(member: MemberQrResponse): void {
  downloadBlob(
    {
      blob: dataUrlToBlob(member.image),
      filename: null,
      contentType: "image/png",
    },
    qrFileName(member.memberName),
  );
}

/**
 * Feature-detect Web Share API file-sharing for this member's QR. `false` when
 * `navigator.share` is missing, or when `navigator.canShare` exists and rejects
 * the file payload. The Share control is hidden (never dead) when this is false.
 */
export function canShareMemberQr(member: MemberQrResponse): boolean {
  if (typeof navigator === "undefined") return false;
  if (typeof navigator.share !== "function") return false;
  if (typeof navigator.canShare === "function") {
    try {
      return navigator.canShare({ files: [memberQrFile(member)] });
    } catch {
      return false;
    }
  }
  return true;
}

/**
 * Share a member's QR image via the Web Share API, guarded by
 * `canShareMemberQr`. A user-cancelled share (`AbortError`) is swallowed; other
 * rejections propagate so the caller can decide.
 */
export async function shareMemberQr(
  member: MemberQrResponse,
  title: string,
  text: string,
): Promise<void> {
  if (!canShareMemberQr(member)) return;
  try {
    await navigator.share({ files: [memberQrFile(member)], title, text });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") return;
    throw error;
  }
}
