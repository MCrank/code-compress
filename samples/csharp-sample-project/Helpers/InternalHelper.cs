using System;

namespace GameProject.Helpers;

/// <summary>
/// File-scoped type visible only within this compilation unit.
/// Demonstrates: file-scoped type modifier.
/// </summary>
file class InternalHelper
{
    public static string FormatDamageText(int damage) =>
        damage > 100 ? "CRITICAL!" : $"{damage} damage";

    public static bool IsValidPlayerName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length is >= 3 and <= 20;
}
