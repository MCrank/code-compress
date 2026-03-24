package com.example.services;

import com.example.models.User;
import com.example.models.Auditable;
import java.util.*;
import java.util.stream.Collectors;

/**
 * Service for managing user operations.
 */
public class UserService implements AutoCloseable {

    private final Map<String, User> users = new HashMap<>();
    private boolean closed = false;

    /**
     * Creates a new user with the given details.
     *
     * @param id the unique identifier
     * @param name the display name
     * @param email the email address
     * @return the created user
     * @throws IllegalStateException if the service is closed
     */
    public User createUser(String id, String name, String email) {
        if (closed) {
            throw new IllegalStateException("Service is closed");
        }
        var user = new User(id, name, email);
        users.put(id, user);
        return user;
    }

    public Optional<User> findById(String id) {
        return Optional.ofNullable(users.get(id));
    }

    /** Finds all users matching the given role. */
    public List<User> findByRole(User.UserRole role) {
        return users.values().stream()
                .filter(u -> u.getRole() == role)
                .collect(Collectors.toList());
    }

    public <T extends Auditable> void audit(T entity) {
        System.out.println("Audit: " + entity.getAuditIdentifier());
    }

    public int getUserCount() {
        return users.size();
    }

    @Override
    public void close() {
        this.closed = true;
        users.clear();
    }
}
