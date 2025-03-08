namespace RamadanReliefAPI.Models.DomainModels;

public class Partners
{
    public Guid Id { get; set; }
    public string OrganizationName { get; set; }
    public string ContactPerson { get; set; }
    public string Phone { get; set; }
    public string Email { get; set; }
    public string? City { get; set; }
    public string Message { get; set; }
    public DateTime RegistrationDate { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}