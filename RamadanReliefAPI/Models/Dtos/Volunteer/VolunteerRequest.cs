using System.ComponentModel.DataAnnotations;

namespace RamadanReliefAPI.Models.Dtos.Volunteer;

public class VolunteerRequest
{
    [Required(ErrorMessage = "Name is required")]
    public string Name { get; set; }
    
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; }
    
    [Required(ErrorMessage = "Phone number is required")]
    public string Phone { get; set; }
    
    public string? City { get; set; }
    
    public string? Message { get; set; }
}