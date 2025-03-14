namespace RamadanReliefAPI.Actors.Messages;

public class DonationFormSubmittedMessage
{
    public string FullName { get; }
    public string Email { get; }
    public string PhoneNumber { get; }
    public decimal Amount { get; }
    public string AdminPhoneNumber { get; }

    public DonationFormSubmittedMessage(string fullName, string email, string phoneNumber, decimal amount, string adminPhoneNumber)
    {
        FullName = fullName;
        Email = email;
        PhoneNumber = phoneNumber;
        Amount = amount;
        AdminPhoneNumber = adminPhoneNumber;
    }
}