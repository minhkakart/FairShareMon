import { setupWorker } from "msw/browser";
import { handlers } from "./handlers";

/** Dev-only MSW worker (started from main.tsx when VITE_ENABLE_MOCKS=true). */
export const worker = setupWorker(...handlers);
