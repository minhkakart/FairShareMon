import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link, useNavigate } from "react-router-dom";
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
import { registerSchema } from "../schemas";
import type { RegisterFormValues } from "../schemas";
import { useRegister } from "../hooks/useAuth";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import { useToast } from "@/app/ToastHost";
import { AuthBrand } from "../components/AuthBrand";

export function RegisterPage() {
  const { t } = useT();
  const navigate = useNavigate();
  const registerMutation = useRegister();
  const toast = useToast();
  const [formError, setFormError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<RegisterFormValues>({ resolver: zodResolver(registerSchema(t)) });

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null);
    try {
      await registerMutation.mutateAsync({
        username: values.username.trim().toLowerCase(),
        password: values.password,
      });
      // No auto-login (backend contract): route to login with a success toast.
      toast.push({ tone: "success", title: t("auth:register.success") });
      void navigate("/login", { replace: true });
    } catch (error) {
      const formLevel = applyFieldErrors(
        error,
        ["username", "password"],
        (field, message) =>
          setError(field as keyof RegisterFormValues, { message }),
      );
      setFormError(formLevel[0] ?? resolveErrorMessage(error, t));
    }
  });

  return (
    <AuthLayout
      header={<AuthBrand subtitle={t("auth:register.subtitle")} />}
      footer={
        <span>
          {t("auth:register.loginPrompt")}{" "}
          <Link to="/login">{t("auth:register.loginLink")}</Link>
        </span>
      }
    >
      <Form onSubmit={onSubmit} noValidate>
        <h1>{t("auth:register.title")}</h1>
        {formError ? <FormError>{formError}</FormError> : null}
        <FieldStack>
          <TextField
            label={t("auth:register.username")}
            hint={t("auth:register.usernameHint")}
            autoComplete="username"
            autoCapitalize="none"
            spellCheck={false}
            required
            error={errors.username?.message}
            {...register("username")}
          />
          <TextField
            label={t("auth:register.password")}
            hint={t("auth:register.passwordHint")}
            type="password"
            autoComplete="new-password"
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
            {t("auth:register.submit")}
          </Button>
        </FormActions>
      </Form>
    </AuthLayout>
  );
}
