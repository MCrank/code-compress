const { isNullOrEmpty } = require("./strings");

/**
 * Formats a user's display name with optional title.
 * @param {string} name - The display name
 * @param {string} [title] - Optional title prefix
 * @returns {string} The formatted name
 */
function formatDisplayName(name, title) {
  if (isNullOrEmpty(name)) {
    return "Unknown";
  }
  return title ? `${title} ${name}` : name;
}

/**
 * Generates a unique identifier.
 * @returns {string} A UUID-like string
 */
const generateId = () => {
  return Date.now().toString(36) + Math.random().toString(36).slice(2);
};

/** Default page size for pagination. */
const DEFAULT_PAGE_SIZE = 25;

class PageResult {
  constructor(items, total, page) {
    this.items = items;
    this.total = total;
    this.page = page;
  }

  get hasMore() {
    return this.page * DEFAULT_PAGE_SIZE < this.total;
  }
}

module.exports = { formatDisplayName, generateId, DEFAULT_PAGE_SIZE, PageResult };
