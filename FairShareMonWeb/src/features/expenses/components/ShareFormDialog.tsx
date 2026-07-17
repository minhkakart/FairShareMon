import { useEffect, useState } from "react";
import { Controller, useForm } from "react-hook-form";
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
  MoneyInput,
  Select,
  TextField,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import { formatMoneyVnd } from "@/i18n/format";
import type { MemberResponse } from "@/features/members/api/types";
import { shareFormSchema } from "../schemas";
import type { ShareFormValues } from "../schemas";
import type { ShareResponse } from "../api/types";
import { useAddShare, useUpdateShare } from "../hooks/useExpenses";
import { buildMemberOptions, makeRenderMemberOption } from "./pickerOptions";

export type ShareFormDialogProps = {
  expenseUuid: string;
  mode: "add" | "edit";
  /** The share being edited (required in "edit" mode). */
  share?: ShareResponse;
  members: MemberResponse[];
  existingShares: ShareResponse[];
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

const KNOWN_FIELDS = ["memberUuid", "amount", "note"];

/**
 * Add / edit a share (B4). The member `Select` excludes members that already have
 * a share (mirrors `7003`), except the row's own member when editing; an
 * owner-representative share has its member locked (mirrors `7002`/the backend
 * change-member block). Errors map `7001`/`7003` → the member field; a stale
 * `7000` and a closed-event `9001` toast + close.
 */
export function ShareFormDialog({
  expenseUuid,
  mode,
  share,
  members,
  existingShares,
  open,
  onOpenChange,
}: ShareFormDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const addShare = useAddShare();
  const updateShare = useUpdateShare();
  const renderMemberOption = makeRenderMemberOption(t);
  const [formError, setFormError] = useState<string | null>(null);

  const lockedMember = mode === "edit" && Boolean(share?.member.isOwnerRepresentative);

  const {
    register,
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<ShareFormValues>({
    resolver: zodResolver(shareFormSchema(t)),
    defaultValues: { memberUuid: "", amount: null, note: "" },
  });

  useEffect(() => {
    if (open) {
      reset(
        mode === "edit" && share
          ? {
              memberUuid: share.member.uuid,
              amount: share.amount,
              note: share.note ?? "",
            }
          : { memberUuid: "", amount: null, note: "" },
      );
      setFormError(null);
    }
  }, [open, mode, share, reset]);

  const takenUuids = new Set(existingShares.map((s) => s.member.uuid));
  if (mode === "edit" && share) takenUuids.delete(share.member.uuid);
  let available = members.filter(
    (m) => !m.isDeleted && !takenUuids.has(m.uuid),
  );
  if (
    mode === "edit" &&
    share &&
    !available.some((m) => m.uuid === share.member.uuid)
  ) {
    available = [share.member, ...available];
  }
  const memberOptions = buildMemberOptions(available);

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null);
    const body = {
      memberUuid: values.memberUuid,
      amount: values.amount ?? 0,
      note: values.note?.trim() ? values.note.trim() : undefined,
    };
    try {
      if (mode === "add") {
        await addShare.mutateAsync({ uuid: expenseUuid, body });
        toast.push({ tone: "success", title: t("expenses:toast.shareAdded") });
      } else if (share) {
        await updateShare.mutateAsync({
          uuid: expenseUuid,
          shareUuid: share.uuid,
          body,
        });
        toast.push({
          tone: "success",
          title: t("expenses:toast.shareUpdated"),
        });
      }
      onOpenChange(false);
    } catch (error) {
      if (isApiError(error)) {
        if (
          error.code === ErrorCodes.ShareMemberInvalid ||
          error.code === ErrorCodes.DuplicateShareMember
        ) {
          setError("memberUuid", { message: error.message });
          return;
        }
        if (
          error.code === ErrorCodes.ShareNotFound ||
          error.code === ErrorCodes.EventClosed ||
          error.code === ErrorCodes.OwnerRepresentativeShareNotDeletable
        ) {
          toast.push({ tone: "danger", title: error.message });
          onOpenChange(false);
          return;
        }
      }
      const formLevel = applyFieldErrors(error, KNOWN_FIELDS, (field, message) =>
        setError(field as keyof ShareFormValues, { message }),
      );
      setFormError(formLevel[0] ?? resolveErrorMessage(error, t));
    }
  });

  const title =
    mode === "add"
      ? t("expenses:shares.addTitle")
      : t("expenses:shares.editTitle");

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title={title} size="md" closeLabel={t("expenses:form.cancel")}>
        <Form onSubmit={onSubmit} noValidate>
          {formError ? <FormError>{formError}</FormError> : null}
          <FieldStack>
            <Controller
              control={control}
              name="memberUuid"
              render={({ field }) => (
                <Select
                  label={t("expenses:shares.memberLabel")}
                  value={field.value || undefined}
                  onValueChange={field.onChange}
                  options={memberOptions}
                  renderOption={renderMemberOption}
                  placeholder={t("expenses:shares.memberPlaceholder")}
                  disabled={lockedMember}
                  hint={lockedMember ? t("expenses:shares.ownerRepEditHint") : undefined}
                  required
                  error={errors.memberUuid?.message}
                />
              )}
            />
            <Controller
              control={control}
              name="amount"
              render={({ field }) => (
                <MoneyInput
                  label={t("expenses:shares.amountLabel")}
                  value={field.value ?? null}
                  onChange={field.onChange}
                  format={formatMoneyVnd}
                  placeholder="0"
                  error={errors.amount?.message}
                />
              )}
            />
            <TextField
              label={t("expenses:shares.noteLabel")}
              placeholder={t("expenses:shares.notePlaceholder")}
              autoComplete="off"
              maxLength={500}
              error={errors.note?.message}
              {...register("note")}
            />
          </FieldStack>
          <DialogFooter>
            <DialogClose asChild>
              <Button type="button" variant="ghost">
                {t("expenses:form.cancel")}
              </Button>
            </DialogClose>
            <Button type="submit" variant="primary" loading={isSubmitting}>
              {mode === "add"
                ? t("expenses:shares.submitAdd")
                : t("expenses:shares.submitEdit")}
            </Button>
          </DialogFooter>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
