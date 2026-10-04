using Kododo.CultureWay.Core.Store;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kododo.Polyglot.Web.History;

public static class HistoryExtensions
{
    /// <summary>
    /// Records every change to the translation store. Call after <c>AddCultureWay</c>, which registers
    /// the store this wraps, and with the auth services, which provide the database it writes to.
    /// </summary>
    public static IServiceCollection AddPolyglotHistory(this IServiceCollection services)
    {
        var store = services.LastOrDefault(d => d.ServiceType == typeof(IStore))
                    ?? throw new InvalidOperationException("AddPolyglotHistory needs the CultureWay store to be registered first.");
        services.RemoveAll<IStore>();

        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IStore>(sp => ActivatorUtilities.CreateInstance<TranslationHistoryStore>(sp, Create(sp, store)));
        services.AddScoped<TranslationHistory>();
        return services;
    }

    private static IStore Create(IServiceProvider sp, ServiceDescriptor descriptor)
        => (IStore)(descriptor.ImplementationInstance
                    ?? descriptor.ImplementationFactory?.Invoke(sp)
                    ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!));
}
