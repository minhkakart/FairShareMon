/// <reference types="node" />
import "@testing-library/jest-dom/vitest";

// Pin the timezone deterministically before any Date/Intl use (money + datetime
// formatters). Locale is pinned per-test via the providers in utils.tsx.
process.env.TZ = "Asia/Ho_Chi_Minh";

import { afterAll, afterEach, beforeAll } from "vitest";
import { cleanup } from "@testing-library/react";
import { server } from "./msw/server";

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  cleanup();
  server.resetHandlers();
});
afterAll(() => server.close());
