using System;
using GameProject.Services;

namespace GameProject.Models;

/// <summary>
/// Represents a player in the game.
/// </summary>
public partial record Player(string Name, int Level, int Health)
{
    /// <summary>
    /// Gets or sets the player's score.
    /// </summary>
    public int Score { get; set; }

    public bool IsAlive => Health > 0;

    public string DisplayName => Name + " (Level " + Level + ")";
}
