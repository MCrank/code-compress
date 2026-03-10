using System;
using System.Collections.Generic;
using System.Linq;
using GameProject.Models;

namespace GameProject.Services;

/// <summary>
/// Provides inventory management operations.
/// </summary>
public class InventoryService
{
    public bool TryAddItem(Inventory inventory, string item)
    {
        if (inventory.Count >= 100)
        {
            return false;
        }

        inventory.AddItem(item);
        return true;
    }

    public List<string> GetSortedItems(Inventory inventory)
    {
        return inventory.Items.OrderBy(i => i, StringComparer.Ordinal).ToList();
    }

    public int GetItemCount(Inventory inventory) => inventory.Count;
}

/// <summary>
/// Extension methods for inventory operations.
/// </summary>
public static class InventoryExtensions
{
    public static bool IsEmpty(this Inventory inventory) => inventory.Count == 0;

    public static bool IsFull(this Inventory inventory) => inventory.Count >= 100;
}
