using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RamadanReliefAPI.Data;
using RamadanReliefAPI.Extensions;
using RamadanReliefAPI.Models;
using RamadanReliefAPI.Models.DomainModels;
using RamadanReliefAPI.Models.Dtos.Donation;
using RamadanReliefAPI.Models.Dtos.Payment;
using RamadanReliefAPI.Services.Interfaces;

namespace RamadanReliefAPI.Controllers;

[ApiController]
[Route("api/donations")]
public class DonationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IPayStackPaymentService _payStackPaymentService;
    private readonly ILogger<DonationsController> _logger;
    private ApiResponse _apiResponse;

    public DonationsController(
        ApplicationDbContext db,
        IPayStackPaymentService payStackPaymentService,
        ILogger<DonationsController> logger)
    {
        _db = db;
        _payStackPaymentService = payStackPaymentService;
        _logger = logger;
        _apiResponse = new ApiResponse();
    }

    /// <summary>
    /// Create a donation and generate a payment link
    /// </summary>
    /// <param name="donationRequest">Donation request</param>
    /// <returns>Payment link</returns>
    [HttpPost("create")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateDonation([FromBody] DonationRequest donationRequest)
    {
        try
        {
            // Generate a unique reference for this transaction
            string reference = $"RR-DON-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            
            // Create and save the donation record with pending status
            var donation = new Donation
            {
                Amount = donationRequest.Amount,
                Currency = "GHS", // Default currency
                DonationDate = DateTime.UtcNow,
                PaymentMethod = donationRequest.PaymentMethod,
                TransactionReference = reference,
                PaymentStatus = CommonConstants.TransactionStatus.Pending,
                DonorName = donationRequest.DonorName,
                DonorEmail = donationRequest.DonorEmail,
                DonorPhone = donationRequest.DonorPhone,
                CampaignSource = donationRequest.CampaignSource
            };
            
            await _db.Donations.AddAsync(donation);
            await _db.SaveChangesAsync();
            
            // Create a temporary user for payment processing
            var tempUser = new User
            {
                Email = donationRequest.DonorEmail ?? "anonymous@ramadanrelief.org",
                Username = donationRequest.DonorName ?? "Anonymous Donor"
            };
            
            // Prepare payment request
            var paymentRequest = new GenericPaymentRequest
            {
                Amount = donationRequest.Amount,
                TicketName = "Ramadan Relief Donation",
                User = tempUser,
                ClientReference = reference,
                IsGroupTicket = false
            };
            
            // Generate payment link
            var paymentResponse = await _payStackPaymentService.CreatePayLink(paymentRequest);
            
            if (!paymentResponse.IsSuccess)
            {
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.BadRequest;
                _apiResponse.Message = "Failed to generate payment link";
                return BadRequest(_apiResponse);
            }
            
            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Message = "Donation initiated successfully";
            _apiResponse.Result = new { 
                PaymentLink = paymentResponse.PayLinkUrl,
                Reference = reference
            };
            
            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing donation");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong while processing your donation";
            _apiResponse.Errors = new List<string> { ex.Message };
            
            return StatusCode(500, _apiResponse);
        }
    }
    
    /// <summary>
    /// Webhook for PayStack to update payment status
    /// </summary>
    /// <returns></returns>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> PayStackWebhook()
    {
        try
        {
            // Read the request body
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            
            // Verify webhook signature (you would need to implement this)
            // ...
            
            // Parse the webhook payload
            var paymentInfo = System.Text.Json.JsonSerializer.Deserialize<PayStackWebhookDto>(body);
            
            if (paymentInfo?.Data?.Reference != null)
            {
                // Find the corresponding donation
                var donation = await _db.Donations.FindAsync(paymentInfo.Data.Reference);
                
                if (donation != null)
                {
                    // Update donation status based on webhook event
                    if (paymentInfo.Event == "charge.success")
                    {
                        donation.PaymentStatus = CommonConstants.TransactionStatus.Success;
                        
                        // Update statistics
                        var stats = await _db.DonationStatistics.FindAsync(1);
                        if (stats == null)
                        {
                            stats = new DonationStatistics
                            {
                                TotalDonations = donation.Amount,
                                TotalDonors = 1,
                                MealsServed = CalculateMeals(donation.Amount),
                                LastUpdated = DateTime.UtcNow
                            };
                            await _db.DonationStatistics.AddAsync(stats);
                        }
                        else
                        {
                            stats.TotalDonations += donation.Amount;
                            stats.TotalDonors += 1;
                            stats.MealsServed += CalculateMeals(donation.Amount);
                            stats.LastUpdated = DateTime.UtcNow;
                        }
                    }
                    else if (paymentInfo.Event == "charge.failed")
                    {
                        donation.PaymentStatus = CommonConstants.TransactionStatus.Failed;
                    }
                    
                    await _db.SaveChangesAsync();
                }
            }
            
            // Always return 200 to acknowledge receipt
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PayStack webhook");
            // Always return 200 to prevent PayStack from retrying
            return Ok();
        }
    }
    
    /// <summary>
    /// Get donation statistics
    /// </summary>
    /// <returns>Donation statistics</returns>
    [HttpGet("statistics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatistics()
    {
        try
        {
            var stats = await _db.DonationStatistics.FindAsync(1);
            
            if (stats == null)
            {
                stats = new DonationStatistics
                {
                    TotalDonations = 0,
                    TotalDonors = 0,
                    MealsServed = 0,
                    LastUpdated = DateTime.UtcNow
                };
            }
            
            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Result = stats;
            
            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching donation statistics");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong";
            _apiResponse.Errors = new List<string> { ex.Message };
            
            return StatusCode(500, _apiResponse);
        }
    }
    
    // Helper method to calculate meals based on donation amount
    private int CalculateMeals(decimal amount)
    {
        // Assuming 1 meal costs 5 GHS
        return (int)(amount / 5);
    }
}

// DTO for PayStack webhook
public class PayStackWebhookDto
{
    public string Event { get; set; }
    public PayStackWebhookDataDto Data { get; set; }
}

public class PayStackWebhookDataDto
{
    public string Reference { get; set; }
    public string Status { get; set; }
}