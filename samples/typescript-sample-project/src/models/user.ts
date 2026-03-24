import { BaseEntity, Auditable, MAX_NAME_LENGTH } from "./entity";

/** User role definitions. */
export enum UserRole {
  Guest = "guest",
  Member = "member",
  Admin = "admin",
}

/** Represents a registered user in the system. */
export class User extends BaseEntity implements Auditable {
  displayName: string;
  email: string;
  role: UserRole;

  constructor(id: string, displayName: string, email: string) {
    super(id);
    this.displayName = displayName;
    this.email = email;
    this.role = UserRole.Member;
  }

  getAuditId(): string {
    return `User:${this.id}`;
  }

  /** Checks if the user has admin privileges. */
  isAdmin(): boolean {
    return this.role === UserRole.Admin;
  }

  setDisplayName(name: string): void {
    if (name.length > MAX_NAME_LENGTH) {
      throw new Error("Display name too long");
    }
    this.displayName = name;
    this.touch();
  }
}

/** Type alias for user lookup results. */
export type UserResult = User | null;

/** Type for user creation parameters. */
export type CreateUserParams = {
  id: string;
  displayName: string;
  email: string;
};
