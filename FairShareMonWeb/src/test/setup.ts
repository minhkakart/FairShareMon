/// <reference types="node" />
import "@testing-library/jest-dom/vitest";

// Pin the timezone deterministically before any Date/Intl use (money + datetime
// formatters). Locale is pinned per-test via the providers in utils.tsx.
process.env.TZ = "Asia/Ho_Chi_Minh";

import { afterAll, afterEach, beforeAll } from "vitest";
import { cleanup } from "@testing-library/react";
import { server } from "./msw/server";

// jsdom polyfills for Radix primitives (Select / Popper) — these DOM APIs are not
// implemented by jsdom, and without them Radix Select cannot open in tests. All
// additive and inert outside the primitives that need them (M4's first pickers).
if (!Element.prototype.hasPointerCapture) {
  Element.prototype.hasPointerCapture = () => false;
}
if (!Element.prototype.setPointerCapture) {
  Element.prototype.setPointerCapture = () => {};
}
if (!Element.prototype.releasePointerCapture) {
  Element.prototype.releasePointerCapture = () => {};
}
if (!Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = () => {};
}
if (typeof globalThis.ResizeObserver === "undefined") {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}

// Object-URL polyfills for the blob → image path (M7 QrDialog renders an <img>
// sourced from `URL.createObjectURL(blob)` and revokes on unmount/refetch; the
// download helper uses the same seam). jsdom implements neither. Additive + inert
// (only defined when absent) — like the Radix polyfills above; tests that need to
// count creates/revokes `vi.spyOn` these afterwards.
let objectUrlSeq = 0;
if (typeof URL.createObjectURL === "undefined") {
  URL.createObjectURL = () => `blob:fsm-test/${(objectUrlSeq += 1)}`;
}
if (typeof URL.revokeObjectURL === "undefined") {
  URL.revokeObjectURL = () => {};
}

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  cleanup();
  server.resetHandlers();
});
afterAll(() => server.close());
