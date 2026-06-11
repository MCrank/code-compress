require_relative 'base_entity'
require_relative 'notification'

# Represents an authenticated user of the system.
class User
  include BaseEntity

  # Maximum allowed length for the display name.
  MAX_NAME_LENGTH = 100

  # Default role assigned to new users.
  DEFAULT_ROLE = :viewer

  attr_reader :id, :email, :name, :role

  # Creates a new User instance.
  def initialize(id, email, name, role = DEFAULT_ROLE)
    @id    = id
    @email = email
    @name  = name
    @role  = role
  end

  # Finds a user by their email address.
  def self.find_by_email(email)
    # Simulated lookup — returns nil if not found.
    nil
  end

  # Creates and returns a new User without persisting it.
  def self.build(attrs = {})
    new(
      attrs.fetch(:id, SecureRandom.uuid),
      attrs.fetch(:email),
      attrs.fetch(:name),
      attrs.fetch(:role, DEFAULT_ROLE)
    )
  end

  # Returns true if the user has admin privileges.
  def admin?
    role == :admin
  end

  # Returns true if the user account is active.
  def active?
    !email.nil? && !email.empty?
  end

  # Returns a greeting string for the user.
  def greet
    "Hello, #{name}!"
  end

  # Returns a notification addressed to this user.
  def notification_for(message)
    Notification.new(id, message)
  end

  # Serialises the user to a plain hash.
  def to_h
    { id: id, email: email, name: name, role: role }
  end

  # Returns a short display string.
  def to_s
    "#{name} <#{email}>"
  end
end
