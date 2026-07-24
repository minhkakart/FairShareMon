import { useEffect } from "react";
import { useParams } from "react-router-dom";
import {
  Button,
  ErrorState,
  LanguageToggle,
  Money,
  Skeleton,
} from "@/components/ui";
import type { Locale } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useLocale } from "@/i18n/LocaleProvider";
import { formatMoneyVnd, formatDateTime } from "@/i18n/format";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import { classifyError } from "@/lib/api/http-error-handling";
import { usePublicShareQuery } from "../hooks/useShare";
import type { PublicEventShareResponse } from "../api/types";
import { PublicBalanceTable } from "../components/PublicBalanceTable";
import styles from "./PublicSharePage.module.css";

/**
 * Minimal standalone layout for the anonymous public page — NOT the authed
 * `AppShellLayout` (no nav, no logout). A quiet header carries the product name
 * and a locale toggle (OQ5) so a mixed-language audience can switch to en-US.
 */
function PublicShareLayout({ children }: { children: React.ReactNode }) {
  const { t } = useT();
  const { locale, setLocale } = useLocale();
  const localeLabels: Record<Locale, string> = {
    "vi-VN": t("common:locale.vi"),
    "en-US": t("common:locale.en"),
  };
  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <div className={styles.brand}>
          <span className={styles.brandName}>{t("common:appName")}</span>
          <span className={styles.brandTagline}>{t("common:tagline")}</span>
        </div>
        <LanguageToggle
          value={locale}
          onChange={setLocale}
          labels={localeLabels}
          groupLabel={t("common:locale.label")}
        />
      </header>
      <main className={styles.main}>{children}</main>
    </div>
  );
}

function LoadingReport() {
  const { t } = useT();
  return (
    <div className={styles.report} aria-busy="true" aria-label={t("share:public.loading")}>
      <Skeleton width="60%" height="2rem" />
      <Skeleton width="40%" height="1.25rem" />
      <div className={styles.summarySkeleton}>
        <Skeleton width="8rem" height="3.5rem" />
        <Skeleton width="8rem" height="3.5rem" />
        <Skeleton width="8rem" height="3.5rem" />
      </div>
      <Skeleton width="100%" height="12rem" />
    </div>
  );
}

function SuccessReport({
  token,
  data,
}: {
  token: string;
  data: PublicEventShareResponse;
}) {
  const { t } = useT();
  return (
    <div className={styles.report}>
      <div className={styles.reportHeader}>
        <h1 className={styles.title}>{data.eventName}</h1>
        {data.closedAt ? (
          <p className={styles.closedAt}>
            {t("share:public.closedAt", {
              time: formatDateTime(data.closedAt),
            })}
          </p>
        ) : null}
      </div>

      <dl className={styles.summary}>
        <div className={styles.summaryItem}>
          <dt className={styles.summaryLabel}>
            {t("share:public.summaryOutstanding")}
          </dt>
          <dd className={styles.summaryValue}>
            <Money amount={data.totalOutstanding} format={formatMoneyVnd} />
          </dd>
        </div>
        <div className={styles.summaryItem}>
          <dt className={styles.summaryLabel}>{t("share:public.summaryOwing")}</dt>
          <dd className={styles.summaryValue}>
            {data.owingMemberCount} {t("share:public.memberUnit")}
          </dd>
        </div>
        <div className={styles.summaryItem}>
          <dt className={styles.summaryLabel}>
            {t("share:public.summarySettled")}
          </dt>
          <dd className={styles.summaryValue}>
            {data.settledMemberCount} {t("share:public.memberUnit")}
          </dd>
        </div>
      </dl>

      <PublicBalanceTable token={token} data={data} />
    </div>
  );
}

/**
 * `/share/:token` — the anonymous public settlement report. Fetches the report
 * with `{ anonymous, skipAuthRefresh }` (`retry: false`). A 16000 / not-found
 * renders ONE friendly "link unavailable" screen with IDENTICAL copy for
 * expired / revoked / missing (no existence leak); a generic failure offers a
 * retry. While mounted, a `noindex,nofollow` robots meta is injected (OQ6) —
 * these temporary URLs expose member names + money.
 */
export function PublicSharePage() {
  const { t } = useT();
  const { token = "" } = useParams();
  const query = usePublicShareQuery(token);

  // OQ6 — keep these temporary financial URLs out of search indexes while the
  // page is mounted; remove the tag on unmount so it never leaks to other views.
  useEffect(() => {
    const meta = document.createElement("meta");
    meta.name = "robots";
    meta.content = "noindex,nofollow";
    document.head.appendChild(meta);
    const previousTitle = document.title;
    document.title = t("share:public.documentTitle");
    return () => {
      document.head.removeChild(meta);
      document.title = previousTitle;
    };
  }, [t]);

  let body: React.ReactNode;
  if (query.isPending) {
    body = <LoadingReport />;
  } else if (query.isError) {
    const notFound =
      (isApiError(query.error) &&
        query.error.code === ErrorCodes.ShareLinkNotFoundOrExpired) ||
      classifyError(query.error) === "notFound";
    if (notFound) {
      body = (
        <div className={styles.report}>
          <ErrorState
            title={t("share:expired.title")}
            description={t("share:expired.body")}
          />
        </div>
      );
    } else {
      body = (
        <div className={styles.report}>
          <ErrorState
            title={t("share:error.title")}
            action={
              <Button variant="secondary" onClick={() => void query.refetch()}>
                {t("share:error.retry")}
              </Button>
            }
          />
        </div>
      );
    }
  } else {
    body = <SuccessReport token={token} data={query.data} />;
  }

  return <PublicShareLayout>{body}</PublicShareLayout>;
}
