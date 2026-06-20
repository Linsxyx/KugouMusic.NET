namespace KgWebApi.Net.Services;

public static class WebApiKuGouServiceCollectionExtensions
{
    [Obsolete("Web API services are composed by WebApiKuGouServiceProvider through Pure.DI.MS.")]
    public static IServiceCollection AddWebApiKuGouServices(this IServiceCollection services)
    {
        return services;
    }
}
