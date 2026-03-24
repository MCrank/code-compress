package service

import (
	"fmt"
	"sync"

	"example.com/project/pkg/models"
)

// UserService manages user operations.
type UserService struct {
	mu    sync.RWMutex
	users map[string]*models.User
}

// NewUserService creates a new UserService instance.
func NewUserService() *UserService {
	return &UserService{
		users: make(map[string]*models.User),
	}
}

// CreateUser creates and stores a new user.
func (s *UserService) CreateUser(id, name, email string) (*models.User, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	user, err := models.NewUser(id, name, email)
	if err != nil {
		return nil, fmt.Errorf("create user: %w", err)
	}

	s.users[id] = user
	return user, nil
}

// FindByID retrieves a user by ID.
func (s *UserService) FindByID(id string) (*models.User, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()

	user, ok := s.users[id]
	return user, ok
}

// Count returns the number of stored users.
func (s *UserService) Count() int {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return len(s.users)
}

// Audit logs an audit entry for any Auditable entity.
func Audit[T models.Auditable](entity T) {
	fmt.Printf("Audit: %s\n", entity.AuditID())
}
