namespace RamadanReliefAPI.Models.Dtos.Donation;

public class DonationResponse
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; }
    public DateTime DonationDate { get; set; }
    public string PaymentMethod { get; set; }
    public string TransactionReference { get; set; }
    public string PaymentStatus { get; set; }
    public string? DonorName { get; set; }
    public string? PaymentLink { get; set; }
}