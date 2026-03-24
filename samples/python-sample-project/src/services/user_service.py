"""User service for managing user operations."""

from typing import Optional

from ..models.user import User, UserRole
from ..models.entity import Auditable


class UserService:
    """Service for managing user operations."""

    def __init__(self) -> None:
        self._users: dict[str, User] = {}

    def create_user(self, user_id: str, name: str, email: str) -> User:
        """Creates a new user with the given details."""
        user = User.create(user_id, name, email)
        self._users[user_id] = user
        return user

    def find_by_id(self, user_id: str) -> Optional[User]:
        """Finds a user by ID."""
        return self._users.get(user_id)

    def find_by_role(self, role: UserRole) -> list[User]:
        """Finds all users matching the given role."""
        return [u for u in self._users.values() if u.role == role]

    def audit(self, entity: Auditable) -> None:
        """Logs an audit entry for any auditable entity."""
        print(f"Audit: {entity.audit_id()}")

    @property
    def user_count(self) -> int:
        """Returns the number of stored users."""
        return len(self._users)


async def create_user_service() -> UserService:
    """Factory function for creating a UserService."""
    return UserService()
