namespace RamadanReliefAPI.Models.Dtos.Partners;

public class PartnerResponse
{
    public Guid Id { get; set; }
    public string OrganizationName { get; set; }
    public string ContactPerson { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string? City { get; set; }
    public string? Message { get; set; }
    public DateTime RegistrationDate { get; set; }
    public bool IsActive { get; set; }
}