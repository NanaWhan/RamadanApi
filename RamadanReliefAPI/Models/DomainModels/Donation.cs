namespace RamadanReliefAPI.Models.DomainModels;

public class Donation
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "GHS";
    public DateTime DonationDate { get; set; }
    public string PaymentMethod { get; set; } // "mobile-money", "bank-transfer", "card-payment"
    public string TransactionReference { get; set; } 
    public string PaymentStatus { get; set; } // "pending", "completed", "failed"
    
    // Optional donor info
    public string? DonorName { get; set; }
    public string? DonorEmail { get; set; }
    public string? DonorPhone { get; set; }
    
    // For campaign tracking
    public string? CampaignSource { get; set; }
}