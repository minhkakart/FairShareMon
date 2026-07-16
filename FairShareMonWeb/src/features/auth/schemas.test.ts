import { describe, expect, it } from "vitest";
import type { z } from "zod";
import {
  changePasswordSchema,
  loginSchema,
  registerSchema,
  utf8ByteLength,
} from "./schemas";
import type { AppTFunction } from "@/i18n/useT";

/**
 * The Zod schemas mirror the backend FluentValidation rules (username 3–32
 * `^[a-zA-Z0-9_.-]+$`, password 8 chars–72 BYTES). Each message key is echoed by
 * the t stub so we assert which rule fired without depending on copy.
 */

const t = ((key: string) => key) as unknown as AppTFunction;

function firstIssueMessage(result: z.ZodSafeParseResult<unknown>) {
  if (result.success) return [];
  return result.error.issues.map((i) => `${String(i.path[0])}:${i.message}`);
}

describe("utf8ByteLength", () => {
  it("Utf8ByteLength_MultibyteChars_CountsBytesNotCharacters", () => {
    expect(utf8ByteLength("abc")).toBe(3);
    // Each Vietnamese "ố" is 3 UTF-8 bytes.
    expect(utf8ByteLength("ố")).toBe(3);
  });
});

describe("registerSchema", () => {
  const schema = registerSchema(t);

  it("RegisterSchema_ValidInput_Passes", () => {
    expect(
      schema.safeParse({ username: "john_doe.01", password: "password1" })
        .success,
    ).toBe(true);
  });

  it("RegisterSchema_UsernameTooShort_FailsLengthRule", () => {
    const result = schema.safeParse({ username: "ab", password: "password1" });
    expect(result.success).toBe(false);
    expect(firstIssueMessage(result)).toContain(
      "username:validation:username.length",
    );
  });

  it("RegisterSchema_UsernameTooLong_FailsLengthRule", () => {
    const result = schema.safeParse({
      username: "a".repeat(33),
      password: "password1",
    });
    expect(result.success).toBe(false);
    expect(firstIssueMessage(result)).toContain(
      "username:validation:username.length",
    );
  });

  it("RegisterSchema_UsernameIllegalChars_FailsFormatRule", () => {
    const result = schema.safeParse({
      username: "bad name!",
      password: "password1",
    });
    expect(result.success).toBe(false);
    expect(firstIssueMessage(result)).toContain(
      "username:validation:username.format",
    );
  });

  it("RegisterSchema_PasswordTooShort_FailsMinRule", () => {
    const result = schema.safeParse({ username: "john", password: "short7!" });
    expect(result.success).toBe(false);
    expect(firstIssueMessage(result)).toContain(
      "password:validation:password.min",
    );
  });

  it("RegisterSchema_PasswordOver72Bytes_FailsMaxBytesRule", () => {
    // 73 ASCII chars = 73 bytes > 72-byte BCrypt cap.
    const result = schema.safeParse({
      username: "john",
      password: "a".repeat(73),
    });
    expect(result.success).toBe(false);
    expect(firstIssueMessage(result)).toContain(
      "password:validation:password.maxBytes",
    );
  });

  it("RegisterSchema_MultibytePasswordExceeding72Bytes_FailsMaxBytesRule", () => {
    // 25 × 3-byte chars = 75 bytes (25 chars) — passes char-count but not bytes.
    const result = schema.safeParse({
      username: "john",
      password: "ố".repeat(25),
    });
    expect(result.success).toBe(false);
    expect(firstIssueMessage(result)).toContain(
      "password:validation:password.maxBytes",
    );
  });

  it("RegisterSchema_Exactly72BytePassword_Passes", () => {
    const result = schema.safeParse({
      username: "john",
      password: "a".repeat(72),
    });
    expect(result.success).toBe(true);
  });
});

describe("loginSchema", () => {
  const schema = loginSchema(t);

  it("LoginSchema_EmptyFields_FailRequiredRules", () => {
    const result = schema.safeParse({ username: "", password: "" });
    expect(result.success).toBe(false);
    const issues = firstIssueMessage(result) ?? [];
    expect(issues).toContain("username:validation:username.required");
    expect(issues).toContain("password:validation:password.required");
  });

  it("LoginSchema_DoesNotEnforceRegisterFormatRules", () => {
    // Login only checks presence — an existing account may predate stricter rules.
    expect(schema.safeParse({ username: "AB", password: "x" }).success).toBe(
      true,
    );
  });
});

describe("changePasswordSchema", () => {
  const schema = changePasswordSchema(t);

  it("ChangePasswordSchema_ValidInput_Passes", () => {
    expect(
      schema.safeParse({
        currentPassword: "oldpass12",
        newPassword: "newpass12",
      }).success,
    ).toBe(true);
  });

  it("ChangePasswordSchema_ShortNewPassword_FailsMinRule", () => {
    const result = schema.safeParse({
      currentPassword: "oldpass12",
      newPassword: "short",
    });
    expect(result.success).toBe(false);
    expect(firstIssueMessage(result)).toContain(
      "newPassword:validation:password.min",
    );
  });
});
