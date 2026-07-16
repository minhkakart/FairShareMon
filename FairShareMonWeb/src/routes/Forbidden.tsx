import { useT } from "@/i18n/useT";
import { ErrorState } from "@/components/ui";

/** Shown when an authenticated user hits an area they are not allowed into. */
export function Forbidden() {
  const { t } = useT();
  return (
    <ErrorState
      title={t("common:forbidden.title")}
      description={t("common:forbidden.body")}
    />
  );
}
