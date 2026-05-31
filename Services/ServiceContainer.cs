namespace BetterWinTab.Services;

public static class ServiceContainer
{
    private static readonly Dictionary<Type, Func<object>> _factories = new();
    private static readonly Dictionary<Type, object> _singletons = new();

    public static void RegisterSingleton<T>(Func<T> factory) where T : class
    {
        _factories[typeof(T)] = () => factory();
    }

    public static void RegisterInstance<T>(T instance) where T : class
    {
        _singletons[typeof(T)] = instance;
    }

    public static T Resolve<T>() where T : class
    {
        if (_singletons.TryGetValue(typeof(T), out var instance))
            return (T)instance;

        if (_factories.TryGetValue(typeof(T), out var factory))
        {
            var created = (T)factory();
            _singletons[typeof(T)] = created;
            return created;
        }

        throw new InvalidOperationException($"Service {typeof(T).Name} is not registered.");
    }

    public static void Reset()
    {
        _factories.Clear();
        _singletons.Clear();
    }
}
