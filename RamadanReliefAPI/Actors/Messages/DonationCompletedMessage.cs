namespace RamadanReliefAPI.Actors.Messages;

public class DonationCompletedMessage
{
    public string TransactionReference { get; }
    public decimal Amount { get; }
    public string DonorPhone { get; }

    public DonationCompletedMessage(string transactionReference, decimal amount, string donorPhone)
    {
        TransactionReference = transactionReference;
        Amount = amount;
        DonorPhone = donorPhone;
    }
}