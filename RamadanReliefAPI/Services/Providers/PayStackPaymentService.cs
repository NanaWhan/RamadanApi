using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PayStack.Net;
using RamadanReliefAPI.Models.Dtos.Payment;
using RamadanReliefAPI.Services.Interfaces;

namespace RamadanReliefAPI.Services.Providers;

public class PayStackPaymentService : IPayStackPaymentService
{
    private PayStackApi PayStackApi { get; set; }
    private readonly ILogger<PayStackPaymentService> _logger;
    private readonly IConfiguration _configuration;

    public PayStackPaymentService(
        ILogger<PayStackPaymentService> logger,
        IConfiguration configuration
    )
    {
        _logger = logger;
        _configuration = configuration;
        // PayStackApi = new PayStackApi(_configuration.GetValue<string>("PayStackKeys:test_key"));
        PayStackApi = new PayStackApi(_configuration.GetValue<string>("PayStackKeys:live_key"));
    }

    public async Task<PayStackResponseDto> CreatePayLink(GenericPaymentRequest request)
    {
        try
        {
            var user = request.User;
            string description;

            // Check if Event is provided (it might be null for direct donations)
            if (request.Event != null)
            {
                var eEvent = request.Event;
                description = $"{request.TicketName} - {eEvent.Title}";
            }
            else
            {
                description = request.TicketName;
            }

            _logger.LogDebug($"Ticket description -- [{description}]");

            // Convert amount to kobo (smallest currency unit)
            var ticketPrice = decimal.Multiply(request.Amount, 100);

            TransactionInitializeRequest payStackRequest = new()
            {
                Currency = "GHS",
                Email = user.Email,
                Channels = new[] { "mobile_money", "card" },
                AmountInKobo = decimal.ToInt32(ticketPrice),
                Reference = request.ClientReference,
                // Fix the syntax error in the callback URL
                CallbackUrl = _configuration.GetValue<string>("PayStackKeys:prod_callback")
            };

            // Add custom fields if needed
            payStackRequest.CustomFields.Add(
                CustomField.From("Ticket", "ticket-type", request.TicketName)
            );

            // Initialize the transaction
            var payStackResponse = PayStackApi.Transactions.Initialize(payStackRequest);

            _logger.LogDebug(
                $"PayStack Response after initialization: {JsonSerializer.Serialize(payStackResponse)}"
            );

            if (!payStackResponse.Status)
            {
                return new PayStackResponseDto()
                {
                    Message = "An error occurred generating pay link",
                    IsSuccess = false
                };
            }

            var payLinkUrl = payStackResponse.Data.AuthorizationUrl;

            return new PayStackResponseDto()
            {
                PayLinkUrl = payLinkUrl,
                IsSuccess = true,
                Message = "Pay link generated"
            };
        }
        catch (Exception e)
        {
            _logger.LogError(e, "An error occurred generating pay link");

            return new PayStackResponseDto()
            {
                IsSuccess = false,
                Message = "An error occurred generating pay link"
            };
        }
    }

    public TransactionVerifyResponse VerifyTransaction(string reference)
    {
        try
        {
            return PayStackApi.Transactions.Verify(reference);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error verifying transaction {reference}");
            return null;
        }
    }
}