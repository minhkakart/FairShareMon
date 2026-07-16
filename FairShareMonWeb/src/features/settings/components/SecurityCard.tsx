import { Link } from "react-router-dom";
import { useT } from "@/i18n/useT";
import { Button, Card, CardBody, CardHeader, Stack } from "@/components/ui";

/**
 * Security section. The destructive, all-devices-sign-out change-password flow
 * keeps its own focused route (OQ3a) — this card links to it with a one-line
 * explanation of the consequence.
 */
export function SecurityCard() {
  const { t } = useT();
  return (
    <Card>
      <CardHeader title={t("settings:security.title")} />
      <CardBody>
        <Stack gap="3">
          <p style={{ margin: 0, color: "var(--fs-color-text-muted)" }}>
            {t("settings:security.changePasswordHint")}
          </p>
          <div>
            <Button asChild variant="secondary" size="sm">
              <Link to="/settings/change-password">
                {t("settings:security.changePassword")}
              </Link>
            </Button>
          </div>
        </Stack>
      </CardBody>
    </Card>
  );
}
