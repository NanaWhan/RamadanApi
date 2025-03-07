namespace RamadanReliefAPI.Models.DomainModels;

public class EventRegistration
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string AttendeeEmail { get; set; }
    public string AttendeeName { get; set; }
    public string AttendeePhone { get; set; }
    public int NumberOfPeople { get; set; }
    public DateTime RegistrationDate { get; set; }
    public Event Event { get; set; }
}