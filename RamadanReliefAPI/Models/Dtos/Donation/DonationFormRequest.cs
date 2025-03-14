using System.ComponentModel.DataAnnotations;

namespace RamadanReliefAPI.Models.Dtos.Donation;

public class DonationFormRequest
{
    [Required(ErrorMessage = "Full name is required")]
    public string FullName { get; set; }
    
    [Required(ErrorMessage = "Email address is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; }
    
    [Required(ErrorMessage = "Phone number is required")]
    public string PhoneNumber { get; set; }
    
    [Required]
    [Range(1, 100000, ErrorMessage = "Amount must be greater than 0")]
    public decimal Amount { get; set; }
}