"""Generic repository interface and in-memory implementation."""

from abc import ABC, abstractmethod
from typing import Generic, TypeVar, Optional

T = TypeVar("T")
ID = TypeVar("ID")


class Repository(ABC, Generic[T, ID]):
    """Generic repository interface for data access."""

    @abstractmethod
    def find_by_id(self, entity_id: ID) -> Optional[T]:
        """Finds an entity by its identifier."""
        ...

    @abstractmethod
    def find_all(self) -> list[T]:
        """Returns all entities."""
        ...

    @abstractmethod
    def save(self, entity: T) -> T:
        """Saves an entity and returns the saved instance."""
        ...

    @abstractmethod
    def delete_by_id(self, entity_id: ID) -> None:
        """Deletes an entity by its identifier."""
        ...

    def count(self) -> int:
        """Returns the count of all entities."""
        return len(self.find_all())
