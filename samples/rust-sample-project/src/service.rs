use std::collections::HashMap;

use crate::models::{Auditable, User, UserError};

/// Generic repository trait for data access.
pub trait Repository<T> {
    /// Finds an entity by its identifier.
    fn find_by_id(&self, id: &str) -> Option<&T>;

    /// Saves an entity.
    fn save(&mut self, entity: T);

    /// Returns the count of stored entities.
    fn count(&self) -> usize;
}

/// Service for managing user operations.
pub struct UserService {
    users: HashMap<String, User>,
}

impl UserService {
    /// Creates a new empty UserService.
    pub fn new() -> Self {
        UserService {
            users: HashMap::new(),
        }
    }

    /// Creates and stores a new user.
    pub fn create_user(&mut self, id: &str, name: &str, email: &str) -> Result<&User, UserError> {
        let user = User::new(id, name, email)?;
        self.users.insert(id.to_string(), user);
        Ok(self.users.get(id).unwrap())
    }

    /// Logs an audit entry for any auditable entity.
    pub fn audit<T: Auditable>(&self, entity: &T) {
        println!("Audit: {}", entity.audit_id());
    }
}

impl Repository<User> for UserService {
    fn find_by_id(&self, id: &str) -> Option<&User> {
        self.users.get(id)
    }

    fn save(&mut self, entity: User) {
        self.users.insert(entity.display_name.clone(), entity);
    }

    fn count(&self) -> usize {
        self.users.len()
    }
}

impl Default for UserService {
    fn default() -> Self {
        Self::new()
    }
}
