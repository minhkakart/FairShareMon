import "i18next";
import type { resources } from "./index";

/**
 * Typed i18n keys: `t("auth:login.title")` is checked, unknown keys error.
 * vi-VN is the reference catalog (default locale, always complete).
 */
declare module "i18next" {
  interface CustomTypeOptions {
    defaultNS: "common";
    resources: (typeof resources)["vi-VN"];
  }
}
