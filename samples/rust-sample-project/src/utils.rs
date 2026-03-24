/// Checks if a string is empty or whitespace-only.
pub fn is_null_or_empty(s: &str) -> bool {
    s.trim().is_empty()
}

/// Truncates a string to the given maximum length.
pub fn truncate(s: &str, max_len: usize) -> &str {
    if s.len() <= max_len {
        s
    } else {
        &s[..max_len]
    }
}

/// Email validation constant.
pub(crate) static EMAIL_SEPARATOR: &str = "@";

/// Type alias for result with string error.
pub type AppResult<T> = Result<T, String>;

/// Validates an email address format.
pub fn is_valid_email(email: &str) -> bool {
    email.contains(EMAIL_SEPARATOR)
}

/// Helper macro for creating formatted error messages.
macro_rules! app_error {
    ($($arg:tt)*) => {
        Err(format!($($arg)*))
    };
}

pub(crate) use app_error;
