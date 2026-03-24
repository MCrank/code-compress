/** Base interface for all identifiable entities. */
export interface Identifiable {
  readonly id: string;
}

/** Audit logging capability. */
export interface Auditable {
  getAuditId(): string;
}

/** Base entity with common fields. */
export abstract class BaseEntity implements Identifiable {
  readonly id: string;
  readonly createdAt: Date;
  updatedAt: Date;

  constructor(id: string) {
    this.id = id;
    this.createdAt = new Date();
    this.updatedAt = this.createdAt;
  }

  /** Updates the timestamp to now. */
  touch(): void {
    this.updatedAt = new Date();
  }
}

/** Maximum name length for display names. */
export const MAX_NAME_LENGTH = 255;

/** Default role assigned to new users. */
export const DEFAULT_ROLE = "member";
