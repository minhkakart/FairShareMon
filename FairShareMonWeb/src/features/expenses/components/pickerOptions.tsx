import type { ReactNode } from "react";
import { Badge, CategoryMarker } from "@/components/ui";
import type { SelectOption } from "@/components/ui";
import type { AppTFunction } from "@/i18n/useT";
import type { MemberResponse } from "@/features/members/api/types";
import type { CategoryResponse } from "@/features/categories/api/types";
import { StarIcon } from "./icons";
import styles from "./pickerOptions.module.css";

export type MemberMeta = { ownerRep: boolean; deleted: boolean };
export type CategoryMeta = {
  color: string;
  icon: string | null;
  isDefault: boolean;
};

/** SelectOptions for members, carrying owner-rep / deleted meta for the renderer. */
export function buildMemberOptions(
  members: MemberResponse[],
): SelectOption<MemberMeta>[] {
  return members.map((m) => ({
    value: m.uuid,
    label: m.name,
    meta: { ownerRep: m.isOwnerRepresentative, deleted: m.isDeleted },
  }));
}

/** SelectOptions for categories, carrying color/icon/default meta for the renderer. */
export function buildCategoryOptions(
  categories: CategoryResponse[],
): SelectOption<CategoryMeta>[] {
  return categories.map((c) => ({
    value: c.uuid,
    label: c.name,
    meta: {
      color: c.color,
      icon: c.icon ?? null,
      isDefault: c.isDefault,
    },
  }));
}

/** Member option renderer: name + owner-rep badge + "(đã xóa)" treatment. */
export function makeRenderMemberOption(t: AppTFunction) {
  return function renderMemberOption(
    option: SelectOption<MemberMeta>,
  ): ReactNode {
    return (
      <span className={styles.memberOption}>
        <span className={styles.memberName}>{option.label}</span>
        {option.meta?.ownerRep ? (
          <Badge tone="info" icon={<StarIcon />}>
            {t("expenses:badge.ownerRep")}
          </Badge>
        ) : null}
        {option.meta?.deleted ? (
          <span className={styles.deletedTag}>{t("expenses:badge.deletedTag")}</span>
        ) : null}
      </span>
    );
  };
}

/** Category option renderer: a CategoryMarker (color + emoji + name). */
export function makeRenderCategoryOption(t: AppTFunction) {
  return function renderCategoryOption(
    option: SelectOption<CategoryMeta>,
  ): ReactNode {
    const meta = option.meta;
    if (!meta) return option.label;
    return (
      <CategoryMarker
        color={meta.color}
        icon={meta.icon}
        name={option.label}
        showLabel
        isDefault={meta.isDefault}
        defaultLabel={t("expenses:badge.defaultCategory")}
      />
    );
  };
}
