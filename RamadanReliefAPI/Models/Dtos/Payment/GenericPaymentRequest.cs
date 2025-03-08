using RamadanReliefAPI.Models.DomainModels;

namespace RamadanReliefAPI.Models.Dtos.Payment;

public class GenericPaymentRequest
{
    public decimal Amount { get; set; }
    public string TicketName { get; set; }
    public Event Event { get; set; }

    public User User { get; set; }

    public Discount? Discount { get; set; }

    public bool IsGroupTicket { get; set; }

    public string ClientReference { get; set; }

}