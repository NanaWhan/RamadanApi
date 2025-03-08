using System.ComponentModel.DataAnnotations;

namespace RamadanReliefAPI.Models.Dtos.Partners;

public class PartnerRequest
{
    [Required(ErrorMessage = "Organization name is required")]
    public string OrganizationName { get; set; }
    
    [Required(ErrorMessage = "Contact person name is required")]
    public string ContactPerson { get; set; }
    
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; }
    
    [Required(ErrorMessage = "Phone number is required")]
    public string Phone { get; set; }
    
    public string? City { get; set; }
    
    public string Message { get; set; }
}