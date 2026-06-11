# Represents a notification sent to a user.
class Notification
  # Severity levels for notifications.
  SEVERITY_INFO    = :info
  SEVERITY_WARNING = :warning
  SEVERITY_ERROR   = :error

  attr_reader :user_id, :message, :severity, :read

  # Creates a new Notification.
  def initialize(user_id, message, severity = SEVERITY_INFO)
    @user_id  = user_id
    @message  = message
    @severity = severity
    @read     = false
  end

  # Marks the notification as read.
  def mark_read!
    @read = true
    self
  end

  # Returns true if this notification represents an error.
  def error?
    severity == SEVERITY_ERROR
  end

  # Returns a formatted display string.
  def to_s
    "[#{severity.upcase}] #{message}"
  end
end
