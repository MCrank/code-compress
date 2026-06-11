# Base module providing common entity behaviour.
module BaseEntity
  # Current schema version for all entities.
  SCHEMA_VERSION = 1

  # Returns the entity type name derived from the class.
  def entity_type
    self.class.name
  end

  # Returns a human-readable summary of the entity.
  def to_summary
    "#{entity_type}##{object_id}"
  end

  # Hook called before the entity is persisted.
  def before_save
    nil
  end

  # Hook called after the entity is loaded from storage.
  def after_load
    nil
  end

  # Returns true if the entity has been persisted.
  def persisted?
    false
  end
end
