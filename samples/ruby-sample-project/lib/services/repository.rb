# Defines the generic storage contract for domain entities.
module Repository
  # Maximum number of records returned by a single query.
  MAX_QUERY_LIMIT = 1000

  # Retrieves an entity by its unique identifier.
  def find(id)
    raise NotImplementedError, "#{self.class}#find not implemented"
  end

  # Retrieves all entities matching the given criteria.
  def find_all(criteria = {})
    raise NotImplementedError, "#{self.class}#find_all not implemented"
  end

  # Persists a new entity and returns it with an assigned id.
  def save(entity)
    raise NotImplementedError, "#{self.class}#save not implemented"
  end

  # Removes the entity with the given id from storage.
  def delete(id)
    raise NotImplementedError, "#{self.class}#delete not implemented"
  end

  # Returns the total number of stored entities.
  def count
    raise NotImplementedError, "#{self.class}#count not implemented"
  end
end
