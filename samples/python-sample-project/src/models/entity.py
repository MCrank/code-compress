"""Base entity module providing common identity and audit types."""

from abc import ABC, abstractmethod
from datetime import datetime
from typing import Optional


# Maximum length for display names.
MAX_NAME_LENGTH: int = 255

DEFAULT_ROLE = "member"


class BaseEntity(ABC):
    """Base class for all domain entities.

    Provides common identity and timestamp fields.
    """

    def __init__(self, entity_id: str) -> None:
        self.id = entity_id
        self.created_at = datetime.now()
        self.updated_at = self.created_at

    def touch(self) -> None:
        """Updates the entity's timestamp to now."""
        self.updated_at = datetime.now()

    @abstractmethod
    def validate(self) -> bool:
        """Validates the entity state."""
        ...


class Auditable(ABC):
    """Interface for entities that support audit logging."""

    @abstractmethod
    def audit_id(self) -> str:
        """Returns the audit trail identifier."""
        ...
