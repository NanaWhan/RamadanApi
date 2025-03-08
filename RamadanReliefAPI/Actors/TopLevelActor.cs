using Akka.Actor;
using Akka.DI.Core;

namespace RamadanReliefAPI.Actors;

public class TopLevelActor
{
    public static IActorRef MainActor = ActorRefs.Nobody;
    public static IActorRef DonationActor = ActorRefs.Nobody;
    public static IActorRef VolunteerActor = ActorRefs.Nobody;
    public static IActorRef PartnerActor = ActorRefs.Nobody;
    public static ActorSystem ActorSystem;

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

    /// <summary>
    /// This method stops the actor system
    /// </summary>
    private static void Stop()
    {
        ActorSystem?.Terminate().Wait(1000);
    }
}