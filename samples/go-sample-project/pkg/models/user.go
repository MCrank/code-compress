package models

import "errors"

// MaxNameLength is the maximum allowed length for display names.
const MaxNameLength = 255

// DefaultRole is assigned to new users.
const DefaultRole = "member"

// ErrNameTooLong is returned when a display name exceeds MaxNameLength.
var ErrNameTooLong = errors.New("display name too long")

// Role represents a user's permission level.
type Role int

const (
	// RoleGuest has read-only access.
	RoleGuest Role = iota
	// RoleMember has standard access.
	RoleMember
	// RoleAdmin has full access.
	RoleAdmin
)

// User represents a registered user in the system.
type User struct {
	Entity
	DisplayName string
	Email       string
	Role        Role
}

// NewUser creates a new user with the given details.
func NewUser(id, name, email string) (*User, error) {
	if len(name) > MaxNameLength {
		return nil, ErrNameTooLong
	}
	return &User{
		Entity:      NewEntity(id),
		DisplayName: name,
		Email:       email,
		Role:        RoleMember,
	}, nil
}

// AuditID returns the audit identifier for this user.
func (u *User) AuditID() string {
	return "User:" + u.ID
}

// IsAdmin checks if the user has admin privileges.
func (u *User) IsAdmin() bool {
	return u.Role == RoleAdmin
}

// SetDisplayName updates the display name with validation.
func (u *User) SetDisplayName(name string) error {
	if len(name) > MaxNameLength {
		return ErrNameTooLong
	}
	u.DisplayName = name
	u.Touch()
	return nil
}
