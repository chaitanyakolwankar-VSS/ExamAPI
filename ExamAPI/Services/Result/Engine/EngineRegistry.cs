using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace ExamAPI.Services.Result.Engine
{
    public class EngineRegistry
    {
        private readonly Dictionary<string, IFactProvider> _factProviders;
        private readonly Dictionary<string, IActionHandler> _actionHandlers;

        public EngineRegistry(IEnumerable<IFactProvider> factProviders, IEnumerable<IActionHandler> actionHandlers)
        {
            _factProviders = factProviders.ToDictionary(p => p.FactName, p => p, StringComparer.OrdinalIgnoreCase);
            _actionHandlers = actionHandlers.ToDictionary(h => h.ActionType, h => h, StringComparer.OrdinalIgnoreCase);
        }

        public IFactProvider? GetFactProvider(string factName)
        {
            return _factProviders.TryGetValue(factName, out var provider) ? provider : null;
        }

        public IActionHandler? GetActionHandler(string actionType)
        {
            return _actionHandlers.TryGetValue(actionType, out var handler) ? handler : null;
        }

        public IEnumerable<string> GetRegisteredFacts() => _factProviders.Keys;
        public IEnumerable<string> GetRegisteredActions() => _actionHandlers.Keys;
    }

    public static class EngineRegistryExtensions
    {
        public static IServiceCollection AddOrdinanceEngine(this IServiceCollection services)
        {
            // Register all FactProviders
            var factProviderType = typeof(IFactProvider);
            var factImplementations = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(s => s.GetTypes())
                .Where(p => factProviderType.IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract);

            foreach (var impl in factImplementations)
            {
                services.AddScoped(factProviderType, impl);
            }

            // Register all ActionHandlers
            var actionHandlerType = typeof(IActionHandler);
            var actionImplementations = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(s => s.GetTypes())
                .Where(p => actionHandlerType.IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract);

            foreach (var impl in actionImplementations)
            {
                services.AddScoped(actionHandlerType, impl);
            }

            services.AddScoped<EngineRegistry>();

            return services;
        }
    }
}
