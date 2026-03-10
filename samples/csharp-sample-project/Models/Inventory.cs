using System;
using System.Collections.Generic;

namespace GameProject.Models;

/// <summary>
/// Manages a player's inventory of items.
/// </summary>
public class Inventory
{
    private readonly List<string> _items = [];

    /// <summary>
    /// Gets the number of items in the inventory.
    /// </summary>
    public int Count => _items.Count;

    public IReadOnlyList<string> Items => _items;

    public void AddItem(string item)
    {
        _items.Add(item);
    }

    public bool RemoveItem(string item)
    {
        return _items.Remove(item);
    }

    public bool Contains(string item)
    {
        return _items.Contains(item);
    }

    /// <summary>
    /// Represents the rarity of an item.
    /// </summary>
    public enum ItemRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }
}
