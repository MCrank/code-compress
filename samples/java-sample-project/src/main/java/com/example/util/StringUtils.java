package com.example.util;

import java.util.regex.Pattern;

/**
 * Utility class for common string operations.
 */
public final class StringUtils {

    /** Pattern for validating email addresses. */
    public static final Pattern EMAIL_PATTERN = Pattern.compile("^[\\w.+-]+@[\\w.-]+\\.[a-zA-Z]{2,}$");

    private StringUtils() {
        // Prevent instantiation
    }

    /**
     * Checks if a string is null or empty.
     */
    public static boolean isNullOrEmpty(String value) {
        return value == null || value.isEmpty();
    }

    /**
     * Truncates a string to the given maximum length.
     */
    public static String truncate(String value, int maxLength) {
        if (value == null || value.length() <= maxLength) {
            return value;
        }
        return value.substring(0, maxLength);
    }

    public static boolean isValidEmail(String email) {
        return email != null && EMAIL_PATTERN.matcher(email).matches();
    }
}
