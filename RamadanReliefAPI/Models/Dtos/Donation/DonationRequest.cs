using System.ComponentModel.DataAnnotations;

namespace RamadanReliefAPI.Models.Dtos.Donation;

public class DonationRequest
{
    [Required]
    [Range(1, 100000, ErrorMessage = "Amount must be greater than 0")]
    public decimal Amount { get; set; }
    
    [Required]
    public string PaymentMethod { get; set; } = "mobile_money"; // Default to mobile money
    
    public string? DonorName { get; set; }
    
    [EmailAddress]
    public string? DonorEmail { get; set; }
    
    public string? DonorPhone { get; set; }
    
    public string? CampaignSource { get; set; }
}