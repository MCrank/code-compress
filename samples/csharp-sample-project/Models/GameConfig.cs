using System;
using System.Collections.Generic;

namespace GameProject.Models;

/// <summary>
/// Delegate for handling game events.
/// </summary>
public delegate void GameEventHandler(string eventName, object? data);

/// <summary>
/// Sealed configuration class with indexer access to settings.
/// Demonstrates: sealed class, delegate, event, indexer.
/// </summary>
public sealed class GameConfig
{
    private readonly Dictionary<string, string> _settings = new();

    /// <summary>
    /// Fired when a configuration value changes.
    /// </summary>
    public event GameEventHandler? OnConfigChanged;

    /// <summary>
    /// Accesses configuration values by key.
    /// </summary>
    public string this[string key]
    {
        get => _settings.TryGetValue(key, out var value) ? value : string.Empty;
        set
        {
            _settings[key] = value;
            OnConfigChanged?.Invoke("ConfigChanged", key);
        }
    }

    public int Count => _settings.Count;

    public bool HasKey(string key) => _settings.ContainsKey(key);
}
