import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  FieldStack,
  Form,
  FormError,
  TextField,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import { resetPasswordSchema } from "../../schemas";
import type { ResetPasswordFormValues } from "../../schemas";
import { generateTempPassword } from "../../generatePassword";
import { useResetPassword } from "../../hooks/useAdminUsers";
import { CheckIcon, CopyIcon, RefreshIcon, WarnIcon } from "../icons";
import type { UserActionDialogProps } from "./DisableUserDialog";
import styles from "../admin.module.css";

/**
 * Reset password (OQ3a — the highest-severity action). The whole body is gated on
 * `open`, so closing the dialog UNMOUNTS it and clears the temp password from
 * state — it is never cached, persisted, or logged.
 */
export function ResetPasswordDialog({
  user,
  open,
  onOpenChange,
}: UserActionDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {open ? (
        <ResetPasswordDialogBody
          user={user}
          onDone={() => onOpenChange(false)}
        />
      ) : null}
    </Dialog>
  );
}

function ResetPasswordDialogBody({
  user,
  onDone,
}: {
  user: { uuid: string; username: string };
  onDone: () => void;
}) {
  const { t } = useT();
  const resetPassword = useResetPassword(user.uuid);

  // Phase 1 = choose/generate; phase 2 = the one-time reveal. `revealed` lives
  // ONLY here in component state (cleared when this body unmounts on close).
  const [phase, setPhase] = useState<"form" | "reveal">("form");
  const [revealed, setRevealed] = useState("");
  const [copied, setCopied] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    setValue,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<ResetPasswordFormValues>({
    resolver: zodResolver(resetPasswordSchema(t)),
    defaultValues: { newPassword: generateTempPassword() },
  });

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null);
    try {
      // The client sends the generated password; the backend echoes it once.
      const result = await resetPassword.mutateAsync(values.newPassword);
      setRevealed(result.password);
      setPhase("reveal");
    } catch (error) {
      if (
        isApiError(error) &&
        (error.code === ErrorCodes.AdminCannotTargetSelf ||
          error.code === ErrorCodes.AdminCannotTargetAdmin)
      ) {
        setFormError(error.message);
        return;
      }
      const formLevel = applyFieldErrors(
        error,
        ["newPassword"],
        (field, message) =>
          setError(field as keyof ResetPasswordFormValues, { message }),
      );
      setFormError(formLevel[0] ?? resolveErrorMessage(error, t));
    }
  });

  async function copy() {
    try {
      await navigator.clipboard.writeText(revealed);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  }

  if (phase === "reveal") {
    return (
      <DialogContent
        tone="danger"
        showClose={false}
        title={t("admin:resetPassword.revealTitle")}
        description={t("admin:resetPassword.revealBody")}
      >
        <div className={styles.secretPanel}>
          <span className={styles.secretLabel}>
            {t("admin:resetPassword.secretLabel", { name: user.username })}
          </span>
          <div className={styles.secretRow}>
            <code className={styles.secretValue}>{revealed}</code>
            <Button
              type="button"
              variant="secondary"
              iconStart={copied ? <CheckIcon /> : <CopyIcon />}
              onClick={() => void copy()}
            >
              {copied
                ? t("admin:resetPassword.copied")
                : t("admin:resetPassword.copy")}
            </Button>
          </div>
          <p className={styles.secretWarning}>
            <span className={styles.secretWarningIcon}>
              <WarnIcon />
            </span>
            {t("admin:resetPassword.warning")}
          </p>
          {/* Live region: announces the copy to assistive tech. */}
          <span className={styles.srOnly} role="status" aria-live="polite">
            {copied ? t("admin:resetPassword.copiedLive") : ""}
          </span>
          {copied ? (
            <span className={styles.copiedNote}>
              <CheckIcon /> {t("admin:resetPassword.copiedNote")}
            </span>
          ) : null}
        </div>
        <DialogFooter>
          <Button type="button" variant="primary" onClick={onDone}>
            {t("admin:resetPassword.close")}
          </Button>
        </DialogFooter>
      </DialogContent>
    );
  }

  return (
    <DialogContent
      tone="danger"
      title={t("admin:resetPassword.title")}
      description={t("admin:resetPassword.body")}
      closeLabel={t("admin:actions.cancel")}
    >
      <Form onSubmit={onSubmit} noValidate>
        {formError ? <FormError>{formError}</FormError> : null}
        <FieldStack>
          <div className={styles.genRow}>
            <div className={styles.genField}>
              <TextField
                label={t("admin:resetPassword.generatedLabel")}
                hint={t("admin:resetPassword.generatedHint")}
                autoComplete="off"
                error={errors.newPassword?.message}
                {...register("newPassword")}
              />
            </div>
            <Button
              type="button"
              variant="secondary"
              iconStart={<RefreshIcon />}
              onClick={() =>
                setValue("newPassword", generateTempPassword(), {
                  shouldValidate: true,
                })
              }
            >
              {t("admin:resetPassword.regenerate")}
            </Button>
          </div>
        </FieldStack>
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              {t("admin:actions.cancel")}
            </Button>
          </DialogClose>
          <Button type="submit" variant="danger" loading={isSubmitting}>
            {t("admin:resetPassword.submit")}
          </Button>
        </DialogFooter>
      </Form>
    </DialogContent>
  );
}
