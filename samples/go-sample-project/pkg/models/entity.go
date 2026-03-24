package models

import (
	"fmt"
	"time"
)

// Entity is the base type for all domain objects.
// It provides common identity and timestamp fields.
type Entity struct {
	ID        string
	CreatedAt time.Time
	UpdatedAt time.Time
}

// NewEntity creates a new Entity with the given ID.
func NewEntity(id string) Entity {
	now := time.Now()
	return Entity{
		ID:        id,
		CreatedAt: now,
		UpdatedAt: now,
	}
}

// Touch updates the entity's UpdatedAt timestamp.
func (e *Entity) Touch() {
	e.UpdatedAt = time.Now()
}

// String returns a human-readable representation.
func (e Entity) String() string {
	return fmt.Sprintf("Entity(%s)", e.ID)
}

// Auditable defines types that support audit logging.
type Auditable interface {
	// AuditID returns the audit trail identifier.
	AuditID() string
}
