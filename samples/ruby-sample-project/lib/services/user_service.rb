require_relative '../models/user'
require_relative '../models/notification'
require_relative 'repository'

# Service layer responsible for user lifecycle operations.
class UserService
  include Repository

  # Minimum required password length.
  MIN_PASSWORD_LENGTH = 8

  # Creates a new UserService backed by the given store.
  def initialize(store)
    @store = store
    @cache = {}
  end

  # Retrieves a user by id, using an in-memory cache.
  def find(id)
    @cache[id] ||= @store.find(id)
  end

  # Returns all users matching optional filter criteria.
  def find_all(criteria = {})
    @store.find_all(criteria)
  end

  # Creates a new user from the given attributes.
  def create_user(attrs)
    validate_attrs!(attrs)
    user = User.build(attrs)
    saved = @store.save(user)
    @cache[saved.id] = saved
    saved
  end

  # Updates mutable fields on an existing user.
  def update_user(id, attrs)
    user = find(id)
    raise ArgumentError, "User not found: #{id}" if user.nil?

    updated = User.new(user.id, attrs.fetch(:email, user.email),
                       attrs.fetch(:name, user.name), attrs.fetch(:role, user.role))
    @store.save(updated)
    @cache[id] = updated
    updated
  end

  # Removes a user from the system.
  def delete(id)
    @cache.delete(id)
    @store.delete(id)
  end

  # Returns the total number of registered users.
  def count
    @store.count
  end

  # Sends a notification to the specified user.
  def notify(user_id, message, severity = Notification::SEVERITY_INFO)
    Notification.new(user_id, message, severity)
  end

  # Returns all admin users.
  def self.admins(users)
    users.select(&:admin?)
  end

  # Validates that a password meets minimum requirements.
  def self.valid_password?(password)
    password.length >= MIN_PASSWORD_LENGTH
  end

  private

  def validate_attrs!(attrs)
    raise ArgumentError, "email is required" if attrs[:email].nil? || attrs[:email].empty?
    raise ArgumentError, "name is required"  if attrs[:name].nil? || attrs[:name].empty?
  end
end
