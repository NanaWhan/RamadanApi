using Akka.Actor;
using Akka.DI.AutoFac;
using Akka.DI.Core;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using RamadanReliefAPI.Actors;
using RamadanReliefAPI.Data;

namespace RamadanReliefAPI.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddActorSystem(this IServiceCollection services, string actorSystemName)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Create the actor system
        var actorSystem = ActorSystem.Create(actorSystemName);
        services.AddSingleton(typeof(ActorSystem), sp => actorSystem);

        // Register the actor system in the TopLevelActor static property
        TopLevelActor.ActorSystem = actorSystem;

        // Configure AutoFac
        var builder = new ContainerBuilder();
        builder.Populate(services);
        
        // Register actor types explicitly
        builder.RegisterType<MainActor>();
        builder.RegisterType<DonationActor>();
        builder.RegisterType<VolunteerActor>();
        builder.RegisterType<PartnerActor>();
        
        var container = builder.Build();

        // Create DI resolver for Akka
        var resolver = new AutoFacDependencyResolver(container, actorSystem);

        // Define supervisor strategy
        var supervisorStrategy = new OneForOneStrategy(
            maxNrOfRetries: 10,
            withinTimeRange: TimeSpan.FromMinutes(1),
            ex => Directive.Restart);

        try
        {
            // Use DI-based actor creation
            TopLevelActor.MainActor = actorSystem.ActorOf(
                actorSystem.DI().Props<MainActor>().WithSupervisorStrategy(supervisorStrategy),
                "MainActor"
            );

            TopLevelActor.DonationActor = actorSystem.ActorOf(
                actorSystem.DI().Props<DonationActor>().WithSupervisorStrategy(supervisorStrategy),
                "DonationActor"
            );

            TopLevelActor.VolunteerActor = actorSystem.ActorOf(
                actorSystem.DI().Props<VolunteerActor>().WithSupervisorStrategy(supervisorStrategy),
                "VolunteerActor"
            );

            TopLevelActor.PartnerActor = actorSystem.ActorOf(
                actorSystem.DI().Props<PartnerActor>().WithSupervisorStrategy(supervisorStrategy),
                "PartnerActor"
            );
        }
        catch (Exception ex)
        {
            // Log the error during actor creation
            Console.WriteLine($"Failed to initialize actors: {ex.Message}");
            throw;
        }

        return services;
    }
}