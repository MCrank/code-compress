package com.example.models;

/**
 * A notification record (Java 16+).
 */
public record Notification(String id, String title, String message, NotificationType type) {

    /** Notification severity types. */
    public enum NotificationType {
        INFO,
        WARNING,
        ERROR
    }

    /** Creates an info notification. */
    public static Notification info(String id, String title, String message) {
        return new Notification(id, title, message, NotificationType.INFO);
    }
}
