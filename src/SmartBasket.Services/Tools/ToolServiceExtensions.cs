using Microsoft.Extensions.DependencyInjection;
using SmartBasket.Services.Tools.Handlers;

namespace SmartBasket.Services.Tools;

public static class ToolServiceExtensions
{
    /// <summary>
    /// Регистрация инструментов для AI Chat
    /// </summary>
    public static IServiceCollection AddTools(this IServiceCollection services)
    {
        // === Tool Handlers ===
        // Универсальные инструменты для работы с БД
        services.AddTransient<IToolHandler, DescribeDataHandler>();
        services.AddTransient<IToolHandler, QueryHandler>();

        // Утилиты
        services.AddTransient<IToolHandler, GetCurrentDateHandler>();
        services.AddTransient<IToolHandler, GetCurrentDateTimeHandler>();

        // === Executor ===
        services.AddTransient<IToolExecutor, ToolExecutor>();

        return services;
    }
}
