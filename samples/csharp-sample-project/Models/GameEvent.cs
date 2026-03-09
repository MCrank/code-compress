using System;

namespace GameProject.Models;

/// <summary>
/// Base class for all game events.
/// </summary>
public abstract class GameEvent
{
    public DateTime Timestamp { get; init; }

    public abstract string EventType { get; }

    public abstract void Execute();
}

/// <summary>
/// Represents a typed game event.
/// </summary>
public class GameEvent<T> : GameEvent where T : class
{
    public T Payload { get; }

    public override string EventType => typeof(T).Name;

    public GameEvent(T payload)
    {
        Payload = payload;
        Timestamp = DateTime.UtcNow;
    }

    public override void Execute()
    {
        // Process the event
    }
}

/// <summary>
/// Defines a handler for game events.
/// </summary>
public interface IGameEventHandler
{
    void Handle(GameEvent gameEvent);

    bool CanHandle(string eventType);
}

public interface IGameEventHandler<T> where T : class
{
    void Handle(GameEvent<T> gameEvent);
}
