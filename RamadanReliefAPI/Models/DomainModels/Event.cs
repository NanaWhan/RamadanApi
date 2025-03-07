namespace RamadanReliefAPI.Models.DomainModels;

public class Event
{
    public Guid Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string Location { get; set; }
    public DateTime EventDate { get; set; }
    public DateTime RegistrationDeadline { get; set; }
    public int MaxAttendees { get; set; }
    public int CurrentAttendees { get; set; } = 0;
    public bool IsActive { get; set; } = true;
}