using PayStack.Net;
using RamadanReliefAPI.Models.Dtos.Payment;

namespace RamadanReliefAPI.Services.Interfaces;

public interface IPayStackPaymentService
{
    Task<PayStackResponseDto> CreatePayLink(GenericPaymentRequest request);
    TransactionVerifyResponse VerifyTransaction(string reference);
}