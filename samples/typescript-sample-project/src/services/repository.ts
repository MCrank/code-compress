/** Generic repository interface for data access. */
export interface Repository<T, ID = string> {
  findById(id: ID): Promise<T | null>;
  findAll(): Promise<T[]>;
  save(entity: T): Promise<T>;
  deleteById(id: ID): Promise<void>;
}

/** Default implementation with in-memory storage. */
export class InMemoryRepository<T extends { id: string }> implements Repository<T> {
  private readonly items = new Map<string, T>();

  async findById(id: string): Promise<T | null> {
    return this.items.get(id) ?? null;
  }

  async findAll(): Promise<T[]> {
    return Array.from(this.items.values());
  }

  async save(entity: T): Promise<T> {
    this.items.set(entity.id, entity);
    return entity;
  }

  async deleteById(id: string): Promise<void> {
    this.items.delete(id);
  }

  /** Returns the count of stored items. */
  get count(): number {
    return this.items.size;
  }
}
