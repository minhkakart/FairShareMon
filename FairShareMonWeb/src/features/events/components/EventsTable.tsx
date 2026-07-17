import { Link } from "react-router-dom";
import {
  Button,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { formatDate } from "@/i18n/format";
import type { EventSummaryResponse } from "../api/types";
import { formatRange } from "../dateRange";
import { EventStatusBadge } from "./EventStatusBadge";
import styles from "./EventsTable.module.css";

export type EventsTableProps = {
  events: EventSummaryResponse[];
};

/**
 * The events list in backend order (`startDate` DESC then `createdAt` DESC —
 * never re-sorted). The name links to the detail route; the date range and
 * created-at use the shared date formatter; status is the color-independent
 * `EventStatusBadge`. Read-only presentational — the page owns data.
 */
export function EventsTable({ events }: EventsTableProps) {
  const { t } = useT();
  return (
    <Table caption={t("events:list.caption")} captionHidden>
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t("events:list.name")}</TableHeaderCell>
          <TableHeaderCell>{t("events:list.range")}</TableHeaderCell>
          <TableHeaderCell>{t("events:list.status")}</TableHeaderCell>
          <TableHeaderCell numeric>
            {t("events:list.expenseCount")}
          </TableHeaderCell>
          <TableHeaderCell>{t("events:list.createdAt")}</TableHeaderCell>
          <TableHeaderCell align="right">
            {t("events:list.actions")}
          </TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {events.map((event) => (
          <TableRow key={event.uuid}>
            <TableHeaderCell scope="row">
              <Link className={styles.nameLink} to={`/events/${event.uuid}`}>
                {event.name}
              </Link>
            </TableHeaderCell>
            <TableCell>{formatRange(event.startDate, event.endDate)}</TableCell>
            <TableCell>
              <EventStatusBadge isClosed={event.isClosed} />
            </TableCell>
            <TableCell numeric>{event.expenseCount}</TableCell>
            <TableCell>{formatDate(event.createdAt)}</TableCell>
            <TableCell actions>
              <Button asChild variant="ghost" size="sm">
                <Link
                  to={`/events/${event.uuid}`}
                  aria-label={t("events:list.viewNamed", { name: event.name })}
                >
                  {t("events:list.view")}
                </Link>
              </Button>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
