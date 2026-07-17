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
import { useDeleteCategory } from "../hooks/useCategories";
import type { CategoryResponse } from "../api/types";

export type DeleteCategoryDialogProps = {
  /** The category to soft-delete (null → dialog closed). */
  category: CategoryResponse | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/**
 * Confirm a soft-delete (OQ5a). The body explains history is preserved (R7). The
 * default category never reaches here (no delete control is rendered); a
 * defensive `4002` (default-not-deletable) and a stale `4000` both surface the
 * localized server message as a toast and close.
 */
export function DeleteCategoryDialog({
  category,
  open,
  onOpenChange,
}: DeleteCategoryDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const deleteCategory = useDeleteCategory();

  async function onConfirm() {
    if (!category) return;
    try {
      await deleteCategory.mutateAsync(category.uuid);
      toast.push({ tone: "success", title: t("categories:toast.deleted") });
    } catch (error) {
      // 4000 (stale list) / 4002 (defensive default) surface localized server
      // text; client-synthetic network/unexpected states fall back to i18n copy.
      toast.push({ tone: "danger", title: resolveErrorMessage(error, t) });
    } finally {
      onOpenChange(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={t("categories:delete.title", { name: category?.name ?? "" })}
        description={t("categories:delete.body")}
        size="sm"
        closeLabel={t("categories:delete.cancel")}
      >
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              {t("categories:delete.cancel")}
            </Button>
          </DialogClose>
          <Button
            type="button"
            variant="danger"
            loading={deleteCategory.isPending}
            onClick={() => void onConfirm()}
          >
            {t("categories:delete.confirmButton")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
