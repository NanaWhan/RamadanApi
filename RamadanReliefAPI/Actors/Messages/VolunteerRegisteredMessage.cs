namespace RamadanReliefAPI.Actors.Messages;

public class VolunteerRegisteredMessage
{
    public Guid VolunteerId { get; }
    public string Name { get; }
    public string Email { get; }
    public string Phone { get; }

    public VolunteerRegisteredMessage(Guid volunteerId, string name, string email, string phone)
    {
        VolunteerId = volunteerId;
        Name = name;
        Email = email;
        Phone = phone;
    }
}