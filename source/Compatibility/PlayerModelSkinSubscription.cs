using System.Reflection;
using Vintagestory.API.Common.Entities;

namespace CombatOverhaul.Compatibility;

internal sealed class PlayerModelSkinSubscription : IDisposable
{
    private const string BehaviorName = "skinnableplayercustommodel";
    private const string OnModelChangedEventName = "OnModelChanged";
    private const string CurrentModelCodePropertyName = "CurrentModelCode";

    private readonly object _skinBehavior;
    private readonly EventInfo _onModelChangedEvent;
    private readonly Action<string> _handler;
    private bool _disposed;

    private PlayerModelSkinSubscription(object skinBehavior, EventInfo onModelChangedEvent, Action<string> handler)
    {
        _skinBehavior = skinBehavior;
        _onModelChangedEvent = onModelChangedEvent;
        _handler = handler;
    }

    public static PlayerModelSkinSubscription? Subscribe(Entity entity, Action<string> onModelChanged, bool invokeCurrentModel)
    {
        EntityBehavior? skinBehavior = entity.GetBehavior(BehaviorName);
        if (skinBehavior == null) return null;

        Type skinBehaviorType = skinBehavior.GetType();
        EventInfo? onModelChangedEvent = skinBehaviorType.GetEvent(OnModelChangedEventName, BindingFlags.Instance | BindingFlags.Public);
        if (onModelChangedEvent == null) return null;

        onModelChangedEvent.AddEventHandler(skinBehavior, onModelChanged);

        if (invokeCurrentModel
            && skinBehaviorType.GetProperty(CurrentModelCodePropertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(skinBehavior) is string modelCode)
        {
            onModelChanged(modelCode);
        }

        return new(skinBehavior, onModelChangedEvent, onModelChanged);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _onModelChangedEvent.RemoveEventHandler(_skinBehavior, _handler);
    }
}
