package util

import "strings"

// IsNullOrEmpty checks if a string is empty or whitespace-only.
func IsNullOrEmpty(s string) bool {
	return strings.TrimSpace(s) == ""
}

// Truncate shortens a string to the given maximum length.
func Truncate(s string, maxLen int) string {
	if len(s) <= maxLen {
		return s
	}
	return s[:maxLen]
}

// emailDomainSeparator is the character separating local and domain parts.
var emailDomainSeparator = "@"

// IsValidEmail performs a basic email format check.
func IsValidEmail(email string) bool {
	return strings.Contains(email, emailDomainSeparator)
}
