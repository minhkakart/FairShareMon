import { Link } from "react-router-dom";
import { Button, Card, EmptyState, ErrorState, Money, Skeleton } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { useEventsQuery } from "@/features/events/hooks/useEvents";
import { EventStatusBadge } from "@/features/events/components/EventStatusBadge";
import { formatRange } from "@/features/events/dateRange";
import type { EventFilter } from "@/features/events/api/types";
import { sortEventsForDashboard } from "../eventOrdering";
import styles from "./dashboard.module.css";

const RECENT_N = 5;
const NO_FILTER: EventFilter = {};

/**
 * Home "Recent events" card: the caller's most relevant events (`GET /events`,
 * no filter → open + closed), ordered open-first then closed and most-recently
 * updated within each group (`sortEventsForDashboard`), sliced to the top 5.
 * Each row links into the event detail and shows name, date range, status badge
 * and the event-level total advanced (VND, rendered verbatim). Mirrors
 * `RecentActivityCard`'s structure + states so it inherits the responsive / a11y
 * behavior for free.
 */
export function RecentEventsCard() {
  const { t } = useT();
  const eventsQuery = useEventsQuery(NO_FILTER);
  const recent = sortEventsForDashboard(eventsQuery.data ?? []).slice(0, RECENT_N);

  return (
    <Card>
      <div className={styles.cardHeadRow}>
        <span className={styles.cardTitle}>{t("common:home.recentEvents")}</span>
        <Link className={styles.viewAll} to="/events">
          {t("common:home.viewAll")}
        </Link>
      </div>

      {eventsQuery.isError ? (
        <ErrorState
          title={t("stats:states.loadError")}
          description={resolveErrorMessage(eventsQuery.error, t)}
          action={
            <Button variant="secondary" onClick={() => void eventsQuery.refetch()}>
              {t("stats:states.retry")}
            </Button>
          }
        />
      ) : eventsQuery.isPending ? (
        <div className={styles.recentList}>
          {Array.from({ length: RECENT_N }).map((_, i) => (
            <div key={i} className={styles.recentRow}>
              <div className={styles.recentMain}>
                <Skeleton width="12rem" />
                <Skeleton width="10rem" />
              </div>
              <Skeleton width="6rem" />
            </div>
          ))}
        </div>
      ) : recent.length === 0 ? (
        <EmptyState title={t("common:home.recentEventsEmpty")} />
      ) : (
        <div className={styles.recentList}>
          {recent.map((event) => (
            <Link
              key={event.uuid}
              className={styles.recentRow}
              to={`/events/${event.uuid}`}
            >
              <span className={styles.recentMain}>
                <span className={styles.recentName}>{event.name}</span>
                <span className={styles.recentMeta}>
                  <span className={styles.recentDate}>
                    {formatRange(event.startDate, event.endDate)}
                  </span>
                  <EventStatusBadge isClosed={event.isClosed} />
                </span>
              </span>
              <Money amount={event.totalAdvanced} className={styles.recentAmount} />
            </Link>
          ))}
        </div>
      )}
    </Card>
  );
}
