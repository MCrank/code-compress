package com.example.services;

import java.lang.annotation.*;

/**
 * Annotation for marking event handler methods.
 */
@Retention(RetentionPolicy.RUNTIME)
@Target(ElementType.METHOD)
public @interface EventHandler {

    /** The event type this handler processes. */
    String value() default "";

    /** Priority order (lower = higher priority). */
    int priority() default 0;
}
