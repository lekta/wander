namespace Wander.Core;

public static class ServiceLocator {
    private static readonly Dictionary<Type, object> _services = new();


    public static void Register<T>(T impl) where T : class {
        ArgumentNullException.ThrowIfNull(impl);
        _services[typeof(T)] = impl;
    }

    public static T Get<T>() where T : class {
        if (_services.TryGetValue(typeof(T), out var service)) {
            return (T)service;
        }

        throw new InvalidOperationException($"Service {typeof(T).Name} is not registered.");
    }

    /// <summary>
    /// The service if it is registered, <c>null</c> otherwise. Written once
    /// here instead of <c>IsRegistered&lt;T&gt;() ? Get&lt;T&gt;() : null</c>
    /// at every call site: the same lookup twice reads as a decision, and a
    /// caller that gets it wrong fails only where the service is missing.
    /// </summary>
    public static T? TryGet<T>() where T : class {
        return _services.TryGetValue(typeof(T), out var service) ? (T)service : null;
    }


    /// <summary>
    /// Whether the service is registered. For the "is it there at all"
    /// question — availability of a command, a menu item. When the service
    /// itself is needed, <see cref="TryGet{T}"/> answers both at once.
    /// </summary>
    public static bool IsRegistered<T>() where T : class {
        return _services.ContainsKey(typeof(T));
    }

    public static void Reset() {
        _services.Clear();
    }
}
