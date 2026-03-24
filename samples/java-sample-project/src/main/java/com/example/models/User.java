package com.example.models;

import java.util.List;
import java.util.ArrayList;

/**
 * Represents a user in the system.
 */
public final class User extends BaseEntity implements Auditable {

    /** Maximum length for display names. */
    public static final int MAX_NAME_LENGTH = 255;

    private static final String DEFAULT_ROLE = "user";

    private String displayName;
    private String email;
    private UserRole role;

    public User(String id, String displayName, String email) {
        super(id);
        this.displayName = displayName;
        this.email = email;
        this.role = UserRole.MEMBER;
    }

    public String getDisplayName() {
        return displayName;
    }

    public void setDisplayName(String displayName) {
        if (displayName != null && displayName.length() > MAX_NAME_LENGTH) {
            throw new IllegalArgumentException("Display name too long");
        }
        this.displayName = displayName;
    }

    public String getEmail() {
        return email;
    }

    /** Returns the user's assigned role. */
    public UserRole getRole() {
        return role;
    }

    public void setRole(UserRole role) {
        this.role = role;
    }

    @Override
    public String getAuditIdentifier() {
        return "User:" + getId();
    }

    /**
     * Role definitions for users.
     */
    public enum UserRole {
        ADMIN,
        MEMBER,
        GUEST;

        public boolean canWrite() {
            return this == ADMIN || this == MEMBER;
        }
    }

    /**
     * Builder for constructing User instances.
     */
    public static class Builder {
        private String id;
        private String displayName;
        private String email;

        public Builder id(String id) {
            this.id = id;
            return this;
        }

        public Builder displayName(String name) {
            this.displayName = name;
            return this;
        }

        public Builder email(String email) {
            this.email = email;
            return this;
        }

        public User build() {
            return new User(id, displayName, email);
        }
    }
}
