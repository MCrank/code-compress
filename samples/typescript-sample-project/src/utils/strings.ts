/** Checks if a value is null, undefined, or empty. */
export function isNullOrEmpty(value: string | null | undefined): boolean {
  return value == null || value.length === 0;
}

/** Truncates a string to the given max length. */
export function truncate(value: string, maxLength: number): string {
  if (value.length <= maxLength) {
    return value;
  }
  return value.substring(0, maxLength);
}

/** Email validation regex pattern. */
export const EMAIL_PATTERN = /^[\w.+-]+@[\w.-]+\.[a-zA-Z]{2,}$/;

/** Validates an email address format. */
export const isValidEmail = (email: string): boolean => EMAIL_PATTERN.test(email);

/** Internal helper — not exported. */
function sanitize(input: string): string {
  return input.replace(/[<>&"']/g, "");
}

const INTERNAL_VERSION = "1.0.0";
