namespace Wander.Core;

public static class ServiceLocator {
    private static readonly Dictionary<Type, object> _services = new();

    // The dictionary is process-wide, and xUnit runs test classes in
    // parallel: ServiceLocatorTests calls Register/Reset while another class
    // reads through ITextSource.Text, and a Dictionary read racing a write
    // is undefined behaviour, not a stale answer. A lock rather than
    // ConcurrentDictionary - the project has no DI container and no
    // concurrent collections, and every method here is a single lookup.
    private static readonly object _gate = new();


    public static void Register<T>(T impl) where T : class {
        ArgumentNullException.ThrowIfNull(impl);
        lock (_gate) {
            _services[typeof(T)] = impl;
        }
    }

    public static T Get<T>() where T : class {
        lock (_gate) {
            if (_services.TryGetValue(typeof(T), out var service)) {
                return (T)service;
            }
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
        lock (_gate) {
            return _services.TryGetValue(typeof(T), out var service) ? (T)service : null;
        }
    }


    /// <summary>
    /// Whether the service is registered. For the "is it there at all"
    /// question — availability of a command, a menu item. When the service
    /// itself is needed, <see cref="TryGet{T}"/> answers both at once.
    /// </summary>
    public static bool IsRegistered<T>() where T : class {
        lock (_gate) {
            return _services.ContainsKey(typeof(T));
        }
    }

    public static void Reset() {
        lock (_gate) {
            _services.Clear();
        }
    }
}
