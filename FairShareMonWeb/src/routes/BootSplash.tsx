import { useT } from "@/i18n/useT";
import { Spinner } from "@/components/ui";

/** Full-viewport loading state shown while the session rehydrates on boot. */
export function BootSplash() {
  const { t } = useT();
  return (
    <div
      style={{
        minHeight: "100dvh",
        display: "grid",
        placeItems: "center",
        gap: "var(--fs-space-3)",
        color: "var(--fs-color-text-muted)",
      }}
    >
      <div
        style={{
          display: "grid",
          justifyItems: "center",
          gap: "var(--fs-space-3)",
        }}
      >
        <Spinner size={28} label={t("common:booting")} />
        <p>{t("common:booting")}</p>
      </div>
    </div>
  );
}
