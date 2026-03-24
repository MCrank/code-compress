/** Notification severity types. */
export type NotificationType = "info" | "warning" | "error";

/** A notification message record. */
export interface Notification {
  readonly id: string;
  readonly title: string;
  readonly message: string;
  readonly type: NotificationType;
}

/** Creates an info notification. */
export function createNotification(
  id: string,
  title: string,
  message: string,
  type: NotificationType = "info"
): Notification {
  return { id, title, message, type };
}

/** Helper to create info notifications. */
export const createInfoNotification = (id: string, title: string, message: string): Notification =>
  createNotification(id, title, message, "info");
