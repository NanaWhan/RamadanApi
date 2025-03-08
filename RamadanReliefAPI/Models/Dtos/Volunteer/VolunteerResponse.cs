namespace RamadanReliefAPI.Models.Dtos.Volunteer;

public class VolunteerResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string? City { get; set; }
    public string? Message { get; set; }
    public DateTime RegistrationDate { get; set; }
    public bool IsActive { get; set; }
}