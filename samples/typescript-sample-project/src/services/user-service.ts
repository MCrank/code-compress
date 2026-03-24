import { User, UserRole, CreateUserParams } from "../models/user";
import { Auditable } from "../models/entity";
import { Repository } from "./repository";

/** Service for managing user operations. */
export class UserService {
  private readonly repo: Repository<User>;

  constructor(repo: Repository<User>) {
    this.repo = repo;
  }

  /** Creates a new user with the given parameters. */
  async createUser(params: CreateUserParams): Promise<User> {
    const user = new User(params.id, params.displayName, params.email);
    return this.repo.save(user);
  }

  async findById(id: string): Promise<User | null> {
    return this.repo.findById(id);
  }

  /** Finds all users with the specified role. */
  async findByRole(role: UserRole): Promise<User[]> {
    const all = await this.repo.findAll();
    return all.filter((u) => u.role === role);
  }

  /** Logs an audit entry for any auditable entity. */
  audit<T extends Auditable>(entity: T): void {
    console.log(`Audit: ${entity.getAuditId()}`);
  }
}

/** Factory function for creating a UserService. */
export function createUserService(repo: Repository<User>): UserService {
  return new UserService(repo);
}

export default UserService;
