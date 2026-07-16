import { setupServer } from "msw/node";
import { handlers } from "./handlers";

/** Node MSW server for the Vitest harness. */
export const server = setupServer(...handlers);
