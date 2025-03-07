namespace RamadanReliefAPI.Actors.Messages;

public struct NewsLetterMessage
{
    public string PhoneNumber { get; set; }

    public NewsLetterMessage(string phoneNumber)
    {
        PhoneNumber = phoneNumber;
    }
}