"""Notification types and factory functions."""

from dataclasses import dataclass
from typing import Literal


NotificationType = Literal["info", "warning", "error"]


@dataclass
class Notification:
    """A notification message record."""
    id: str
    title: str
    message: str
    type: NotificationType = "info"


def create_notification(
    notification_id: str,
    title: str,
    message: str,
    notification_type: NotificationType = "info",
) -> Notification:
    """Creates a new notification instance."""
    return Notification(
        id=notification_id,
        title=title,
        message=message,
        type=notification_type,
    )
