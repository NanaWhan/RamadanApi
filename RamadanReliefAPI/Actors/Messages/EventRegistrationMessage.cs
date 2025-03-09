namespace RamadanReliefAPI.Actors.Messages;

public class EventRegistrationMessage
{
    public Guid EventId { get; }
    public string AttendeeName { get; }
    public string PhoneNumber { get; }
    public int NumberOfPeople { get; }

    public EventRegistrationMessage(Guid eventId, string attendeeName, string phoneNumber, int numberOfPeople)
    {
        EventId = eventId;
        AttendeeName = attendeeName;
        PhoneNumber = phoneNumber;
        NumberOfPeople = numberOfPeople;
    }
}