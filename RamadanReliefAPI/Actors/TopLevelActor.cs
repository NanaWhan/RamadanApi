using Akka.Actor;
using Akka.DI.Core;

namespace RamadanReliefAPI.Actors;

public class TopLevelActor
{
    public static IActorRef MainActor { get; set; } = ActorRefs.Nobody;
    public static IActorRef DonationActor { get; set; } = ActorRefs.Nobody;
    public static IActorRef VolunteerActor { get; set; } = ActorRefs.Nobody;
    public static IActorRef PartnerActor { get; set; } = ActorRefs.Nobody;
    public static ActorSystem ActorSystem { get; set; }

    public static IActorRef GetActorInstance<T>(string name)
        where T : ActorBase =>
        ActorSystem.ActorOf(
            ActorSystem.DI().Props<T>().WithSupervisorStrategy(GetDefaultSupervisorStrategy),
            name
        );

    public static SupervisorStrategy GetDefaultSupervisorStrategy =>
        new OneForOneStrategy(
            3,
            TimeSpan.FromSeconds(3),
            ex =>
            {
                if (!(ex is ActorInitializationException))
                    return Directive.Resume;
                Stop();
                return Directive.Stop;
            }
        );

    private static void Stop()
    {
        ActorSystem?.Terminate().Wait(1000);
    }
}