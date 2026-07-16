import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useT } from "@/i18n/useT";
import {
  AuthLayout,
  Button,
  FieldStack,
  Form,
  FormActions,
  FormError,
  TextField,
} from "@/components/ui";
import { loginSchema } from "../schemas";
import type { LoginFormValues } from "../schemas";
import { useLogin } from "../hooks/useAuth";
import { getSession } from "@/lib/auth/session";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import { AuthBrand } from "../components/AuthBrand";

export function LoginPage() {
  const { t } = useT();
  const navigate = useNavigate();
  const location = useLocation();
  const login = useLogin();
  const [formError, setFormError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormValues>({ resolver: zodResolver(loginSchema(t)) });

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null);
    const username = values.username.trim().toLowerCase();
    try {
      const tokens = await login.mutateAsync({
        username,
        password: values.password,
      });
      getSession().setSession(tokens, { username });
      const from = (location.state as { from?: string } | null)?.from;
      void navigate(from ?? "/dashboard", { replace: true });
    } catch (error) {
      const formLevel = applyFieldErrors(
        error,
        ["username", "password"],
        (field, message) =>
          setError(field as keyof LoginFormValues, { message }),
      );
      setFormError(formLevel[0] ?? resolveErrorMessage(error, t));
    }
  });

  return (
    <AuthLayout
      header={<AuthBrand subtitle={t("auth:login.subtitle")} />}
      footer={
        <span>
          {t("auth:login.registerPrompt")}{" "}
          <Link to="/register">{t("auth:login.registerLink")}</Link>
        </span>
      }
    >
      <Form onSubmit={onSubmit} noValidate>
        <h1>{t("auth:login.title")}</h1>
        {formError ? <FormError>{formError}</FormError> : null}
        <FieldStack>
          <TextField
            label={t("auth:login.username")}
            autoComplete="username"
            autoCapitalize="none"
            spellCheck={false}
            required
            error={errors.username?.message}
            {...register("username")}
          />
          <TextField
            label={t("auth:login.password")}
            type="password"
            autoComplete="current-password"
            required
            error={errors.password?.message}
            {...register("password")}
          />
        </FieldStack>
        <FormActions>
          <Button
            type="submit"
            variant="primary"
            fullWidth
            loading={isSubmitting}
          >
            {t("auth:login.submit")}
          </Button>
        </FormActions>
      </Form>
    </AuthLayout>
  );
}
