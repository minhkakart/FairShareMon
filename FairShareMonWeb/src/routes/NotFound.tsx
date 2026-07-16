import { Link } from "react-router-dom";
import { useT } from "@/i18n/useT";
import { Button } from "@/components/ui";

/**
 * Shared not-found view. Used both as the `*` route AND rendered directly by
 * feature screens for ownership 404s / code 1003 — so existence is never leaked
 * (a resource you don't own looks identical to one that doesn't exist).
 */
export function NotFound() {
  const { t } = useT();
  return (
    <div
      style={{
        display: "grid",
        gap: "var(--fs-space-4)",
        justifyItems: "start",
        maxWidth: "36rem",
        margin: "0 auto",
        padding: "var(--fs-space-8) 0",
      }}
    >
      <h1>{t("common:notFound.title")}</h1>
      <p style={{ color: "var(--fs-color-text-muted)" }}>
        {t("common:notFound.body")}
      </p>
      <Link to="/dashboard">
        <Button variant="secondary">{t("common:notFound.backHome")}</Button>
      </Link>
    </div>
  );
}
