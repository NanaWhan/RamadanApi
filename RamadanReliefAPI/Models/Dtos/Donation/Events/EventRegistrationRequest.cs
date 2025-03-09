using System.ComponentModel.DataAnnotations;

namespace RamadanReliefAPI.Models.Dtos.Donation.Events;

public class EventRegistrationRequest
{
    [Required(ErrorMessage = "Full name is required")]
    public string FullName { get; set; }
    
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; }
    
    [Required(ErrorMessage = "Phone number is required")]
    public string PhoneNumber { get; set; }
    
    [Required(ErrorMessage = "Number of people is required")]
    [Range(1, 20, ErrorMessage = "Number of people must be between 1 and 20")]
    public int NumberOfPeople { get; set; }
    
    [Required(ErrorMessage = "Event ID is required")]
    public Guid EventId { get; set; }
}