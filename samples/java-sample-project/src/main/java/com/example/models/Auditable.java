package com.example.models;

/**
 * Interface for entities that support audit logging.
 */
public interface Auditable {

    /** Returns a unique identifier for audit trail purposes. */
    String getAuditIdentifier();
}
