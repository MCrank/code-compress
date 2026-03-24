package service

// Repository defines generic data access operations.
type Repository[T any, ID comparable] interface {
	// FindByID retrieves an entity by its identifier.
	FindByID(id ID) (T, error)

	// FindAll returns all entities.
	FindAll() ([]T, error)

	// Save persists an entity and returns the saved instance.
	Save(entity T) (T, error)

	// DeleteByID removes an entity by its identifier.
	DeleteByID(id ID) error
}
