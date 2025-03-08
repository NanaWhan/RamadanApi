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
    public static IServiceCollection AddActorSystem(
        this IServiceCollection services,
        string actorSystemName
    )
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        var actorSystem = ActorSystem.Create(actorSystemName);
        services.AddSingleton(typeof(ActorSystem), sp => actorSystem);

        var builder = new ContainerBuilder();
        builder.Populate(services);

        builder.RegisterType<TopLevelActor>();
        builder.RegisterType<MainActor>();
        builder.RegisterType<DonationActor>();
        builder.Register(context =>
        {
            // Your DbContext configuration logic here (e.g., connection string)
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(
                    "User Id=postgres.samobihwxdfcbxpmqkqc;Password=mezttr8q3x9OuBhQ;Server=aws-0-eu-central-1.pooler.supabase.com;Port=5432;Database=postgres"
                )
                .Options;
            return options;
        });
        

        var container = builder.Build();

        var resolver = new AutoFacDependencyResolver(container, actorSystem);

        TopLevelActor.ActorSystem = actorSystem;

        TopLevelActor.MainActor = actorSystem.ActorOf(
            actorSystem
                .DI()
                .Props<MainActor>()
                .WithSupervisorStrategy(TopLevelActor.GetDefaultSupervisorStrategy),
            nameof(MainActor)
        );
        
        TopLevelActor.DonationActor = actorSystem.ActorOf(
            actorSystem
                .DI()
                .Props<DonationActor>()
                .WithSupervisorStrategy(TopLevelActor.GetDefaultSupervisorStrategy),
            nameof(DonationActor)
        );

        return services;
    }
}