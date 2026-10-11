using Events.Application.Abstractions;
using Events.Application.Events;
using Events.Application.Public;
using Events.Application.Venues;
using Events.Infrastructure.Database;
using Events.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Events.Infrastructure;

public static class Setup
{
    public static IServiceCollection AddEvents(this IServiceCollection services)
    {
        services.AddEventsPersistence();

        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IVenueRepository, VenueRepository>();
        services.AddScoped<IEventsUnitOfWork, EventsUnitOfWork>();

        services.AddScoped<VenueService>();
        services.AddScoped<EventService>();
        services.AddScoped<PublicEventService>();

        return services;
    }
}
