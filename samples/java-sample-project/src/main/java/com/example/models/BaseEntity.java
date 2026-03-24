package com.example.models;

import java.time.Instant;
import java.util.Objects;

/**
 * Base class for all domain entities.
 * Provides common identity and audit fields.
 */
public abstract class BaseEntity {

    private final String id;
    private Instant createdAt;
    private Instant updatedAt;

    /** Creates a new entity with the given ID. */
    protected BaseEntity(String id) {
        this.id = Objects.requireNonNull(id, "id must not be null");
        this.createdAt = Instant.now();
        this.updatedAt = this.createdAt;
    }

    public String getId() {
        return id;
    }

    public Instant getCreatedAt() {
        return createdAt;
    }

    public Instant getUpdatedAt() {
        return updatedAt;
    }

    /** Marks the entity as updated at the current time. */
    public void touch() {
        this.updatedAt = Instant.now();
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (!(o instanceof BaseEntity that)) return false;
        return Objects.equals(id, that.id);
    }

    @Override
    public int hashCode() {
        return Objects.hash(id);
    }
}
