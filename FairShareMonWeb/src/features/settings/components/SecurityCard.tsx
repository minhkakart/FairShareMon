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
            <Link to="/settings/change-password">
              <Button variant="secondary" size="sm">
                {t("settings:security.changePassword")}
              </Button>
            </Link>
          </div>
        </Stack>
      </CardBody>
    </Card>
  );
}
