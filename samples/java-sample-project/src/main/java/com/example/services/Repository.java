package com.example.services;

import java.util.List;
import java.util.Optional;

/**
 * Generic repository interface for data access.
 *
 * @param <T> the entity type
 * @param <ID> the identifier type
 */
public interface Repository<T, ID> {

    /** Finds an entity by its identifier. */
    Optional<T> findById(ID id);

    /** Returns all entities. */
    List<T> findAll();

    /** Saves an entity and returns the saved instance. */
    T save(T entity);

    /** Deletes an entity by its identifier. */
    void deleteById(ID id);

    /** Returns the count of all entities. */
    default long count() {
        return findAll().size();
    }
}
