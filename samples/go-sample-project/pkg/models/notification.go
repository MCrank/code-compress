package models

// NotificationType represents the severity of a notification.
type NotificationType string

const (
	NotificationInfo    NotificationType = "info"
	NotificationWarning NotificationType = "warning"
	NotificationError   NotificationType = "error"
)

// Notification holds a single notification message.
type Notification struct {
	ID      string
	Title   string
	Message string
	Type    NotificationType
}

// NewInfoNotification creates an info-level notification.
func NewInfoNotification(id, title, message string) Notification {
	return Notification{
		ID:      id,
		Title:   title,
		Message: message,
		Type:    NotificationInfo,
	}
}
