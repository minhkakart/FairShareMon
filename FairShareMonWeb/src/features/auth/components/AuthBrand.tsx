import { useT } from "@/i18n/useT";

/** Brand lockup shown above the auth cards (app name + tagline + page subtitle). */
export function AuthBrand({ subtitle }: { subtitle?: string }) {
  const { t } = useT();
  return (
    <div>
      <strong style={{ fontSize: "var(--fs-text-xl)" }}>
        {t("common:appName")}
      </strong>
      <p style={{ color: "var(--fs-color-text-muted)", margin: 0 }}>
        {subtitle ?? t("common:tagline")}
      </p>
    </div>
  );
}
