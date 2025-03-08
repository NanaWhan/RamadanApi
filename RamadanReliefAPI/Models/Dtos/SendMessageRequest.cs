using System.ComponentModel.DataAnnotations;

namespace RamadanReliefAPI.Models.Dtos;

public class SendMessageRequest
{
    [Required(ErrorMessage = "Message is required")]
    public string Message { get; set; }
}