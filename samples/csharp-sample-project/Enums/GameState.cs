using System;

namespace GameProject.Enums;

/// <summary>
/// Represents the current state of the game.
/// </summary>
public enum GameState
{
    NotStarted,
    InProgress,
    Paused,
    Completed
}

/// <summary>
/// Flags for player capabilities.
/// </summary>
[Flags]
public enum PlayerCapabilities
{
    None = 0,
    CanAttack = 1,
    CanHeal = 2,
    CanTrade = 4,
    CanCraft = 8
}
