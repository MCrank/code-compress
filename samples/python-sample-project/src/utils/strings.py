"""Utility functions for common string operations."""

import re
from typing import Optional


EMAIL_PATTERN = re.compile(r"^[\w.+-]+@[\w.-]+\.[a-zA-Z]{2,}$")


def is_null_or_empty(value: Optional[str]) -> bool:
    """Checks if a string is None or empty."""
    return value is None or len(value) == 0


def truncate(value: str, max_length: int) -> str:
    """Truncates a string to the given maximum length."""
    if len(value) <= max_length:
        return value
    return value[:max_length]


def is_valid_email(email: str) -> bool:
    """Validates an email address format."""
    return EMAIL_PATTERN.match(email) is not None


def _sanitize(value: str) -> str:
    """Internal helper to sanitize input strings."""
    return re.sub(r"[<>&\"']", "", value)
