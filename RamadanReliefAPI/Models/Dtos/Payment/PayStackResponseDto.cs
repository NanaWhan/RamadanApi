namespace RamadanReliefAPI.Models.Dtos.Payment;

public class PayStackResponseDto
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; }
    public string? PayLinkUrl { get; set; }
}