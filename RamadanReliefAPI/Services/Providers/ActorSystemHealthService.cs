using Akka.Actor;
using RamadanReliefAPI.Actors;

namespace RamadanReliefAPI.Services.Providers;

public class ActorSystemHealthService : IHostedService
{
    private readonly ILogger<ActorSystemHealthService> _logger;

    public ActorSystemHealthService(ILogger<ActorSystemHealthService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking actor system health on startup...");

        var actorStatus = new Dictionary<string, bool>
        {
            ["MainActor"] = TopLevelActor.MainActor != ActorRefs.Nobody,
            ["DonationActor"] = TopLevelActor.DonationActor != ActorRefs.Nobody,
            ["VolunteerActor"] = TopLevelActor.VolunteerActor != ActorRefs.Nobody,
            ["PartnerActor"] = TopLevelActor.PartnerActor != ActorRefs.Nobody
        };

        foreach (var actor in actorStatus)
        {
            _logger.LogInformation($"{actor.Key} initialized: {actor.Value}");
        }

        if (actorStatus.Values.All(v => v))
        {
            _logger.LogInformation("All actors successfully initialized!");
        }
        else
        {
            _logger.LogWarning("Some actors failed to initialize!");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}