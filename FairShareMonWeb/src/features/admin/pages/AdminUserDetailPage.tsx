import { Link, useParams } from "react-router-dom";
import {
  Button,
  Card,
  CardBody,
  DescriptionList,
  DescriptionRow,
  EmptyState,
  ErrorState,
  Skeleton,
  Stack,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { formatDate } from "@/i18n/format";
import { useAdminUserQuery } from "../hooks/useAdminUsers";
import { AdminTierBadge } from "../components/AdminTierBadge";
import { RoleBadge } from "../components/RoleBadge";
import { StatusBadge } from "../components/StatusBadge";
import { GrantHistoryTable } from "../components/users/GrantHistoryTable";
import { AdminUserActions } from "../components/users/AdminUserActions";
import { UsersIcon } from "../components/icons";
import styles from "../components/admin.module.css";

/**
 * /admin/users/:uuid — metadata + grant history + the sensitive-action bar.
 * A `14000` miss → an admin-LOCAL not-found (the admin scope may confirm a user
 * exists, so this is NOT the ledger existence-hiding 404). Metadata + grants only
 * (R10).
 */
export function AdminUserDetailPage() {
  const { t } = useT();
  const { uuid = "" } = useParams();
  const query = useAdminUserQuery(uuid, Boolean(uuid));

  const backLink = (
    <Button asChild variant="ghost" size="sm">
      <Link to="/admin/users">{t("admin:detail.back")}</Link>
    </Button>
  );

  if (query.isPending) {
    return (
      <Stack gap="4">
        {backLink}
        <Card>
          <CardBody>
            <Skeleton width="60%" height="1.5rem" />
          </CardBody>
        </Card>
      </Stack>
    );
  }

  if (query.isError) {
    // 14000 → admin-local not-found; anything else → a generic error + retry.
    if (isApiError(query.error) && query.error.code === ErrorCodes.AdminUserNotFound) {
      return (
        <Stack gap="4">
          {backLink}
          <Card>
            <CardBody>
              <EmptyState
                icon={<UsersIcon />}
                title={t("admin:detail.notFound.title")}
                description={t("admin:detail.notFound.body")}
                action={
                  <Button asChild variant="secondary" size="sm">
                    <Link to="/admin/users">
                      {t("admin:detail.notFound.back")}
                    </Link>
                  </Button>
                }
              />
            </CardBody>
          </Card>
        </Stack>
      );
    }
    return (
      <Stack gap="4">
        {backLink}
        <ErrorState
          title={t("admin:detail.states.error")}
          description={resolveErrorMessage(query.error, t)}
          action={
            <Button variant="secondary" onClick={() => void query.refetch()}>
              {t("admin:detail.states.retry")}
            </Button>
          }
        />
      </Stack>
    );
  }

  const user = query.data;

  return (
    <Stack gap="4">
      {backLink}

      <Card>
        <CardBody>
          <h2 className={styles.panelTitle}>{t("admin:detail.metadata.title")}</h2>
          <DescriptionList>
            <DescriptionRow term={t("admin:detail.metadata.username")}>
              {user.username}
            </DescriptionRow>
            <DescriptionRow term={t("admin:detail.metadata.uuid")}>
              <span className={styles.mono}>{user.uuid}</span>
            </DescriptionRow>
            <DescriptionRow term={t("admin:detail.metadata.tier")}>
              <AdminTierBadge tier={user.tier} />
            </DescriptionRow>
            <DescriptionRow term={t("admin:detail.metadata.role")}>
              <RoleBadge role={user.role} />
            </DescriptionRow>
            <DescriptionRow term={t("admin:detail.metadata.status")}>
              <StatusBadge status={user.status} />
            </DescriptionRow>
            <DescriptionRow term={t("admin:detail.metadata.createdAt")}>
              <span className={styles.mono}>{formatDate(user.createdAt)}</span>
            </DescriptionRow>
          </DescriptionList>
        </CardBody>
      </Card>

      <Card>
        <CardBody>
          <h2 className={styles.panelTitle}>
            {t("admin:detail.actionsTitle")}
          </h2>
          <AdminUserActions user={user} />
        </CardBody>
      </Card>

      <Card>
        <CardBody>
          <h2 className={styles.panelTitle}>
            {t("admin:detail.grantHistory.title")}
          </h2>
          <GrantHistoryTable grants={user.grants} username={user.username} />
        </CardBody>
      </Card>
    </Stack>
  );
}
