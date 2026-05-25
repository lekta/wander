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

    public static bool IsRegistered<T>() where T : class {
        return _services.ContainsKey(typeof(T));
    }

    public static void Reset() {
        _services.Clear();
    }
}
