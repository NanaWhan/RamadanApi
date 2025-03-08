namespace RamadanReliefAPI.Models.DomainModels;

public class Volunteer
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string? City { get; set; }
    public string? Message { get; set; }
    public DateTime RegistrationDate { get; set; }
    public bool IsActive { get; set; } = true;
}

