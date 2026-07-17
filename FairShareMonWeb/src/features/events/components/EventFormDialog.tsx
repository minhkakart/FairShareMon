import { useEffect, useState } from "react";
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
  LimitNotice,
  TextField,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import { eventFormSchema } from "../schemas";
import type { EventFormValues } from "../schemas";
import type {
  CreateEventRequest,
  EventResponse,
  UpdateEventRequest,
} from "../api/types";
import { useCreateEvent, useUpdateEvent } from "../hooks/useEvents";
import { dateInputToIso, isoToDateInput } from "../dateRange";

export type EventFormDialogProps = {
  /** Present → edit that event; absent → create a new (open) event. */
  event?: EventResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Called with the new event's uuid after a successful create (e.g. to navigate). */
  onCreated?: (uuid: string) => void;
};

const KNOWN_FIELDS = ["name", "description", "startDate", "endDate"];

function emptyDefaults(): EventFormValues {
  return { name: "", description: "", startDate: "", endDate: "" };
}

function toDefaults(event?: EventResponse): EventFormValues {
  if (!event) return emptyDefaults();
  return {
    name: event.name,
    description: event.description ?? "",
    startDate: isoToDateInput(event.startDate),
    endDate: isoToDateInput(event.endDate),
  };
}

/**
 * Shared create/edit dialog (OQ2a). Create → `POST /events` (new events are
 * always open); edit → `PUT /events/:uuid` (open-only). Dates submit as
 * noon-anchored ISO (OQ5a). Errors: `13001` (create) → inline LimitNotice
 * (OQ9a, form stays mounted); `9003` (range excludes an assigned expense) →
 * form-level message; `9001` (edit a closed event) / `9000` (stale) → toast +
 * close; `1001` → `applyFieldErrors`; else a form-level error.
 */
export function EventFormDialog({
  event,
  open,
  onOpenChange,
  onCreated,
}: EventFormDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const createEvent = useCreateEvent();
  const updateEvent = useUpdateEvent();
  const isEdit = Boolean(event);

  const [formError, setFormError] = useState<string | null>(null);
  const [limitReached, setLimitReached] = useState(false);

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<EventFormValues>({
    resolver: zodResolver(eventFormSchema(t)),
    defaultValues: toDefaults(event),
  });

  useEffect(() => {
    if (open) {
      reset(toDefaults(event));
      setFormError(null);
      setLimitReached(false);
    }
  }, [open, event, reset]);

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null);
    setLimitReached(false);
    const description = values.description?.trim()
      ? values.description.trim()
      : undefined;

    try {
      if (event) {
        const body: UpdateEventRequest = {
          name: values.name.trim(),
          description,
          startDate: dateInputToIso(values.startDate),
          endDate: dateInputToIso(values.endDate),
        };
        await updateEvent.mutateAsync({ uuid: event.uuid, body });
        toast.push({ tone: "success", title: t("events:toast.updated") });
        onOpenChange(false);
      } else {
        const body: CreateEventRequest = {
          name: values.name.trim(),
          description,
          startDate: dateInputToIso(values.startDate),
          endDate: dateInputToIso(values.endDate),
        };
        const created = await createEvent.mutateAsync(body);
        toast.push({ tone: "success", title: t("events:toast.created") });
        onOpenChange(false);
        onCreated?.(created.uuid);
      }
    } catch (error) {
      if (isApiError(error)) {
        if (error.code === ErrorCodes.OpenEventLimitReached) {
          setLimitReached(true);
          return;
        }
        if (
          error.code === ErrorCodes.EventClosed ||
          error.code === ErrorCodes.EventNotFound
        ) {
          toast.push({ tone: "danger", title: error.message });
          onOpenChange(false);
          return;
        }
        if (error.code === ErrorCodes.EventRangeExcludesAssignedExpenses) {
          setFormError(error.message);
          return;
        }
      }
      const formLevel = applyFieldErrors(error, KNOWN_FIELDS, (field, message) =>
        setError(field as keyof EventFormValues, { message }),
      );
      setFormError(formLevel[0] ?? resolveErrorMessage(error, t));
    }
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={isEdit ? t("events:form.editTitle") : t("events:form.createTitle")}
        size="md"
        closeLabel={t("events:form.cancel")}
      >
        <Form onSubmit={onSubmit} noValidate>
          {limitReached ? (
            <LimitNotice
              title={t("events:limit.title")}
              description={t("events:limit.body")}
            />
          ) : null}
          {formError ? <FormError>{formError}</FormError> : null}

          <FieldStack>
            <TextField
              label={t("events:form.nameLabel")}
              placeholder={t("events:form.namePlaceholder")}
              required
              error={errors.name?.message}
              {...register("name")}
            />
            <TextField
              label={t("events:form.descriptionLabel")}
              placeholder={t("events:form.descriptionPlaceholder")}
              error={errors.description?.message}
              {...register("description")}
            />
            <TextField
              label={t("events:form.startLabel")}
              type="date"
              required
              error={errors.startDate?.message}
              {...register("startDate")}
            />
            <TextField
              label={t("events:form.endLabel")}
              type="date"
              required
              error={errors.endDate?.message}
              {...register("endDate")}
            />
          </FieldStack>

          <DialogFooter>
            <DialogClose asChild>
              <Button type="button" variant="ghost">
                {t("events:form.cancel")}
              </Button>
            </DialogClose>
            <Button type="submit" variant="primary" loading={isSubmitting}>
              {isEdit ? t("events:form.submitEdit") : t("events:form.submitCreate")}
            </Button>
          </DialogFooter>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
