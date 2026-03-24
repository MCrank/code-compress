"""User model with role management."""

from enum import Enum
from typing import Optional

from .entity import BaseEntity, Auditable, MAX_NAME_LENGTH


class UserRole(Enum):
    """User role definitions."""
    GUEST = "guest"
    MEMBER = "member"
    ADMIN = "admin"


class User(BaseEntity, Auditable):
    """Represents a registered user in the system."""

    def __init__(self, user_id: str, display_name: str, email: str) -> None:
        super().__init__(user_id)
        self.display_name = display_name
        self.email = email
        self.role = UserRole.MEMBER

    def audit_id(self) -> str:
        """Returns the audit identifier for this user."""
        return f"User:{self.id}"

    def is_admin(self) -> bool:
        """Checks if the user has admin privileges."""
        return self.role == UserRole.ADMIN

    def set_display_name(self, name: str) -> None:
        """Updates the display name with validation."""
        if len(name) > MAX_NAME_LENGTH:
            raise ValueError("Display name too long")
        self.display_name = name
        self.touch()

    def validate(self) -> bool:
        """Validates the user entity."""
        return bool(self.display_name) and bool(self.email)

    @staticmethod
    def create(user_id: str, name: str, email: str) -> "User":
        """Factory method to create a validated user."""
        user = User(user_id, name, email)
        if not user.validate():
            raise ValueError("Invalid user data")
        return user
