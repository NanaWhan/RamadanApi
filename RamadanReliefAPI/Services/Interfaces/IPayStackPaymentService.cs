using RamadanReliefAPI.Models.Dtos.Payment;

namespace RamadanReliefAPI.Services.Interfaces;

public interface IPayStackPaymentService
{
    Task<PayStackResponseDto> CreatePayLink(GenericPaymentRequest request);
}