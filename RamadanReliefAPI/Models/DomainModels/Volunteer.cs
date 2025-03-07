namespace RamadanReliefAPI.Models.DomainModels;

public class Volunteer
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string City { get; set; }
    public List<string> Interests { get; set; } = new List<string>();
    public List<string> Availability { get; set; } = new List<string>();
    public string? Message { get; set; }
    public DateTime RegistrationDate { get; set; }
    public bool IsActive { get; set; } = true;
}