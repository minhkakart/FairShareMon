import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./styles/global.css";
import "@/i18n"; // initialize i18next (side-effecting) before first render
import { AppProviders } from "@/app/providers";

async function enableMocks(): Promise<void> {
  if (!import.meta.env.DEV || import.meta.env.VITE_ENABLE_MOCKS !== "true") {
    return;
  }
  const { worker } = await import("@/test/msw/browser");
  await worker.start({ onUnhandledRequest: "bypass" });
}

void enableMocks().then(() => {
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <AppProviders />
    </StrictMode>,
  );
});
