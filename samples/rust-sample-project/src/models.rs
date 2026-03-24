use std::fmt;
use std::time::SystemTime;

/// Base trait for all entities with an identity.
pub trait Identifiable {
    /// Returns the unique identifier.
    fn id(&self) -> &str;
}

/// Trait for entities that support audit logging.
pub trait Auditable: Identifiable {
    /// Returns the audit trail identifier.
    fn audit_id(&self) -> String;
}

/// Maximum length for display names.
pub const MAX_NAME_LENGTH: usize = 255;

/// Default role assigned to new users.
const DEFAULT_ROLE: &str = "member";

/// User role definitions.
#[derive(Debug, Clone, PartialEq)]
pub enum Role {
    Guest,
    Member,
    Admin,
}

/// A registered user in the system.
#[derive(Debug, Clone)]
pub struct User {
    id: String,
    pub display_name: String,
    pub email: String,
    pub role: Role,
    created_at: SystemTime,
}

impl User {
    /// Creates a new user with the given details.
    pub fn new(id: &str, display_name: &str, email: &str) -> Result<Self, UserError> {
        if display_name.len() > MAX_NAME_LENGTH {
            return Err(UserError::NameTooLong);
        }
        Ok(User {
            id: id.to_string(),
            display_name: display_name.to_string(),
            email: email.to_string(),
            role: Role::Member,
            created_at: SystemTime::now(),
        })
    }

    /// Checks if the user has admin privileges.
    pub fn is_admin(&self) -> bool {
        self.role == Role::Admin
    }

    /// Updates the display name with validation.
    pub fn set_display_name(&mut self, name: &str) -> Result<(), UserError> {
        if name.len() > MAX_NAME_LENGTH {
            return Err(UserError::NameTooLong);
        }
        self.display_name = name.to_string();
        Ok(())
    }

    fn validate_email(email: &str) -> bool {
        email.contains('@')
    }
}

impl Identifiable for User {
    fn id(&self) -> &str {
        &self.id
    }
}

impl Auditable for User {
    fn audit_id(&self) -> String {
        format!("User:{}", self.id)
    }
}

impl fmt::Display for User {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "User({})", self.id)
    }
}

/// Errors that can occur during user operations.
#[derive(Debug)]
pub enum UserError {
    NameTooLong,
    InvalidEmail,
    NotFound,
}

impl fmt::Display for UserError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            UserError::NameTooLong => write!(f, "display name too long"),
            UserError::InvalidEmail => write!(f, "invalid email address"),
            UserError::NotFound => write!(f, "user not found"),
        }
    }
}
