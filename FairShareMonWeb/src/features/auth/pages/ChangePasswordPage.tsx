import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useNavigate } from "react-router-dom";
import { useT } from "@/i18n/useT";
import {
  Button,
  Card,
  CardBody,
  FieldStack,
  Form,
  FormActions,
  FormError,
  TextField,
} from "@/components/ui";
import { changePasswordSchema } from "../schemas";
import type { ChangePasswordFormValues } from "../schemas";
import { useChangePassword } from "../hooks/useAuth";
import { getSession } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import { useToast } from "@/app/ToastHost";

export function ChangePasswordPage() {
  const { t } = useT();
  const navigate = useNavigate();
  const changePassword = useChangePassword();
  const toast = useToast();
  const [formError, setFormError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<ChangePasswordFormValues>({
    resolver: zodResolver(changePasswordSchema(t)),
  });

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null);
    try {
      await changePassword.mutateAsync({
        currentPassword: values.currentPassword,
        newPassword: values.newPassword,
      });
      // Backend revokes ALL tokens on password change — force a fresh login.
      getSession().clearSession();
      queryClient.clear();
      toast.push({
        tone: "success",
        title: t("auth:changePassword.reloginNotice"),
      });
      void navigate("/login", { replace: true });
    } catch (error) {
      const formLevel = applyFieldErrors(
        error,
        ["currentPassword", "newPassword"],
        (field, message) =>
          setError(field as keyof ChangePasswordFormValues, { message }),
      );
      setFormError(formLevel[0] ?? resolveErrorMessage(error, t));
    }
  });

  return (
    <Card style={{ maxWidth: "32rem" }}>
      <CardBody>
        <h1>{t("auth:changePassword.title")}</h1>
        <p style={{ color: "var(--fs-color-text-muted)", marginTop: 0 }}>
          {t("auth:changePassword.subtitle")}
        </p>
        <Form onSubmit={onSubmit} noValidate>
          {formError ? <FormError>{formError}</FormError> : null}
          <FieldStack>
            <TextField
              label={t("auth:changePassword.current")}
              type="password"
              autoComplete="current-password"
              required
              error={errors.currentPassword?.message}
              {...register("currentPassword")}
            />
            <TextField
              label={t("auth:changePassword.new")}
              type="password"
              autoComplete="new-password"
              required
              error={errors.newPassword?.message}
              {...register("newPassword")}
            />
          </FieldStack>
          <FormActions>
            <Button type="submit" variant="primary" loading={isSubmitting}>
              {t("auth:changePassword.submit")}
            </Button>
          </FormActions>
        </Form>
      </CardBody>
    </Card>
  );
}
