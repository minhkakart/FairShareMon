import { useT } from "@/i18n/useT";
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
} from "@/components/ui";
import { useToast } from "@/app/ToastHost";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { useDeleteTag } from "../hooks/useTags";
import type { TagResponse } from "../api/types";

export type DeleteTagDialogProps = {
  /** The tag to soft-delete (null → dialog closed). */
  tag: TagResponse | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/**
 * Confirm a soft-delete (OQ5a). The body explains history is preserved (R7). A
 * stale `5000` (deleted elsewhere) surfaces the localized server message as a
 * toast and closes.
 */
export function DeleteTagDialog({ tag, open, onOpenChange }: DeleteTagDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const deleteTag = useDeleteTag();

  async function onConfirm() {
    if (!tag) return;
    try {
      await deleteTag.mutateAsync(tag.uuid);
      toast.push({ tone: "success", title: t("tags:toast.deleted") });
    } catch (error) {
      toast.push({ tone: "danger", title: resolveErrorMessage(error, t) });
    } finally {
      onOpenChange(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={t("tags:delete.title", { name: tag?.name ?? "" })}
        description={t("tags:delete.body")}
        size="sm"
        closeLabel={t("tags:delete.cancel")}
      >
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              {t("tags:delete.cancel")}
            </Button>
          </DialogClose>
          <Button
            type="button"
            variant="danger"
            loading={deleteTag.isPending}
            onClick={() => void onConfirm()}
          >
            {t("tags:delete.confirmButton")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
