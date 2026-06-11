# Utility methods for string manipulation.
module StringUtils
  # Default truncation suffix appended when a string is shortened.
  TRUNCATION_SUFFIX = "..."

  # Truncates text to the given length, appending a suffix if needed.
  def self.truncate(text, max_length, suffix = TRUNCATION_SUFFIX)
    return text if text.length <= max_length

    text[0, max_length - suffix.length] + suffix
  end

  # Converts a string to snake_case.
  def self.to_snake_case(str)
    str.gsub(/([A-Z]+)([A-Z][a-z])/, '\1_\2')
       .gsub(/([a-z\d])([A-Z])/, '\1_\2')
       .downcase
  end

  # Converts a string to CamelCase (PascalCase).
  def self.to_camel_case(str)
    str.split('_').map(&:capitalize).join
  end

  # Returns true if the string is blank (nil, empty, or only whitespace).
  def self.blank?(str)
    str.nil? || str.strip.empty?
  end

  # Removes all non-alphanumeric characters and lowercases the result.
  def self.slugify(str)
    str.downcase.gsub(/[^a-z0-9]+/, '-').gsub(/^-|-$/, '')
  end
end
