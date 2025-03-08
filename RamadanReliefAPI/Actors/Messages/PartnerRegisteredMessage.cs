namespace RamadanReliefAPI.Actors.Messages;

public class PartnerRegisteredMessage
{
    public Guid PartnerId { get; }
    public string OrganizationName { get; }
    public string ContactPerson { get; }
    public string Email { get; }
    public string Phone { get; }

    public PartnerRegisteredMessage(Guid partnerId, string organizationName, string contactPerson, string email, string phone)
    {
        PartnerId = partnerId;
        OrganizationName = organizationName;
        ContactPerson = contactPerson;
        Email = email;
        Phone = phone;
    }
}