using System.Net;
using Akka.Actor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RamadanReliefAPI.Actors;
using RamadanReliefAPI.Actors.Messages;
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
            _logger.LogInformation($"Creating new donation with reference: {reference}");
            
            // Create and save the donation record with pending status
            var donation = new Donation
            {
                Amount = donationRequest.Amount,
                Currency = "GHS",
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
            _logger.LogInformation($"Donation record created in database with id: {donation.Id}");
            
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
            _logger.LogInformation($"Generating payment link for reference: {reference}");
            var paymentResponse = await _payStackPaymentService.CreatePayLink(paymentRequest);
            
            if (!paymentResponse.IsSuccess)
            {
                _logger.LogWarning($"Failed to generate payment link for reference: {reference}. Message: {paymentResponse.Message}");
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.BadRequest;
                _apiResponse.Message = "Failed to generate payment link";
                return BadRequest(_apiResponse);
            }
            
            _logger.LogInformation($"Payment link generated successfully for reference: {reference}. URL: {paymentResponse.PayLinkUrl}");
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
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> PayStackWebhook()
    {
        string rawBody = string.Empty;
        
        try
        {
            // Read the request body
            using var reader = new StreamReader(Request.Body);
            rawBody = await reader.ReadToEndAsync();
            
            _logger.LogInformation($"Received PayStack webhook. Raw payload: {rawBody}");
            
            // Parse the webhook payload
            var paymentInfo = System.Text.Json.JsonSerializer.Deserialize<PayStackWebhookDto>(rawBody);
            
            _logger.LogInformation($"Parsed webhook data. Event: {paymentInfo?.Event}, Reference: {paymentInfo?.Data?.Reference}, Status: {paymentInfo?.Data?.Status}");
            
            if (paymentInfo?.Data?.Reference != null)
            {
                string reference = paymentInfo.Data.Reference;
                
                // Find the corresponding donation
                var donation = await _db.Donations.FirstOrDefaultAsync(d => 
                    d.TransactionReference == reference);
                
                if (donation == null)
                {
                    _logger.LogWarning($"Donation not found for reference: {reference}");
                    return Ok(); // Return OK to prevent PayStack from retrying
                }
                
                _logger.LogInformation($"Found donation for reference: {reference}. Current status: {donation.PaymentStatus}");
                
                // Update donation status based on webhook event
                if (paymentInfo.Event == "charge.success")
                {
                    _logger.LogInformation($"Processing successful payment for reference: {reference}");
                    
                    // Begin transaction to ensure data consistency
                    using var transaction = await _db.Database.BeginTransactionAsync();
                    
                    try
                    {
                        // Only update status if not already successful
                        if (donation.PaymentStatus != CommonConstants.TransactionStatus.Success)
                        {
                            _logger.LogInformation($"Updating donation status to SUCCESS for reference: {reference}");
                            donation.PaymentStatus = CommonConstants.TransactionStatus.Success;
                            await _db.SaveChangesAsync();
                            
                            // Tell the actor system about the completed donation
                            _logger.LogInformation($"Sending message to DonationActor for reference: {reference}");
                            TopLevelActor.DonationActor.Tell(new DonationCompletedMessage(
                                donation.TransactionReference,
                                donation.Amount,
                                donation.DonorPhone
                            ));
                            
                            _logger.LogInformation($"Message sent to actor system for reference: {reference}");
                        }
                        else
                        {
                            _logger.LogInformation($"Donation already marked as successful for reference: {reference}");
                        }
                        
                        await transaction.CommitAsync();
                        _logger.LogInformation($"Transaction committed for reference: {reference}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error updating donation status for reference: {reference}");
                        await transaction.RollbackAsync();
                        _logger.LogInformation($"Transaction rolled back for reference: {reference}");
                        // Don't rethrow - we still want to return OK to PayStack
                    }
                }
                else if (paymentInfo.Event == "charge.failed")
                {
                    _logger.LogInformation($"Updating donation status to FAILED for reference: {reference}");
                    donation.PaymentStatus = CommonConstants.TransactionStatus.Failed;
                    await _db.SaveChangesAsync();
                }
                else 
                {
                    _logger.LogInformation($"Received unhandled event type: {paymentInfo.Event} for reference: {reference}");
                }
            }
            else
            {
                _logger.LogWarning("Webhook payload doesn't contain a reference");
            }
            
            // Always return 200 to acknowledge receipt
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing PayStack webhook. Raw payload: {rawBody}");
            // Always return 200 to prevent PayStack from retrying
            return Ok();
        }
    }
    
    /// <summary>
    /// Manual check to verify payment status for a donation
    /// </summary>
    [HttpGet("check-status/{reference}")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckPaymentStatus(string reference)
    {
        try
        {
            _logger.LogInformation($"Checking payment status for reference: {reference}");
            
            var donation = await _db.Donations.FirstOrDefaultAsync(d => d.TransactionReference == reference);
            
            if (donation == null)
            {
                _logger.LogWarning($"Donation not found for reference: {reference}");
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.NotFound;
                _apiResponse.Message = "Donation not found";
                return NotFound(_apiResponse);
            }
            
            _logger.LogInformation($"Found donation for reference: {reference}. Current status: {donation.PaymentStatus}");
            
            // Verify with PayStack too
            var verification = _payStackPaymentService.VerifyTransaction(reference);
            _logger.LogInformation($"PayStack verification response: {System.Text.Json.JsonSerializer.Serialize(verification)}");
            
            if (verification != null && verification.Status)
            {
                _logger.LogInformation($"PayStack verification status: {verification.Data.Status}");
                
                // If PayStack says it's successful but our record doesn't, update it
                if (verification.Data.Status == "success" && donation.PaymentStatus != CommonConstants.TransactionStatus.Success)
                {
                    _logger.LogInformation($"Payment verified as successful by PayStack but not in our system. Updating status for reference: {reference}");
                    
                    // Begin transaction to ensure data consistency
                    using var transaction = await _db.Database.BeginTransactionAsync();
                    
                    try
                    {
                        donation.PaymentStatus = CommonConstants.TransactionStatus.Success;
                        await _db.SaveChangesAsync();
                        
                        // Tell the actor system about the completed donation
                        TopLevelActor.DonationActor.Tell(new DonationCompletedMessage(
                            donation.TransactionReference,
                            donation.Amount,
                            donation.DonorPhone
                        ));
                        
                        await transaction.CommitAsync();
                        _logger.LogInformation($"Updated donation status to SUCCESS for reference: {reference}");
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(ex, $"Error updating donation status for reference: {reference}");
                        throw;
                    }
                }
            }
            
            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Message = "Payment status retrieved";
            _apiResponse.Result = new
            {
                PaymentStatus = donation.PaymentStatus,
                PayStackStatus = verification?.Data.Status ?? "unknown",
                Amount = donation.Amount,
                Date = donation.DonationDate
            };
            
            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking payment status for reference: {reference}");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong";
            _apiResponse.Errors = new List<string> { ex.Message };
            
            return StatusCode(500, _apiResponse);
        }
    }
    
    /// <summary>
    /// Manual verification endpoint for admin use
    /// </summary>
    [HttpPost("verify-payment/{reference}")]
    [Authorize(Roles = CommonConstants.Roles.Admin)]
    public async Task<IActionResult> VerifyPayment(string reference)
    {
        try
        {
            _logger.LogInformation($"Manual verification requested for reference: {reference}");
            
            var donation = await _db.Donations.FirstOrDefaultAsync(d => d.TransactionReference == reference);
            
            if (donation == null)
            {
                _logger.LogWarning($"Donation not found for reference: {reference}");
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.NotFound;
                _apiResponse.Message = "Donation not found";
                return NotFound(_apiResponse);
            }
            
            _logger.LogInformation($"Found donation for reference: {reference}. Current status: {donation.PaymentStatus}");
            
            // Already successful, no need to verify again
            if (donation.PaymentStatus == CommonConstants.TransactionStatus.Success)
            {
                _logger.LogInformation($"Donation already marked as successful for reference: {reference}");
                _apiResponse.IsSuccess = true;
                _apiResponse.StatusCode = HttpStatusCode.OK;
                _apiResponse.Message = "Payment already verified as successful";
                return Ok(_apiResponse);
            }
            
            // Verify the payment with PayStack directly
            _logger.LogInformation($"Verifying payment with PayStack for reference: {reference}");
            var verification = _payStackPaymentService.VerifyTransaction(reference);
            
            if (verification == null)
            {
                _logger.LogWarning($"PayStack verification returned null for reference: {reference}");
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.BadRequest;
                _apiResponse.Message = "Payment verification failed - PayStack returned null";
                return BadRequest(_apiResponse);
            }
            
            _logger.LogInformation($"PayStack verification response. Status: {verification.Status}, Data.Status: {verification.Data?.Status}");
            
            if (verification.Status && verification.Data.Status == "success")
            {
                _logger.LogInformation($"PayStack verified payment as successful for reference: {reference}");
                
                // Begin transaction to ensure data consistency
                using var transaction = await _db.Database.BeginTransactionAsync();
                
                try
                {
                    // Update donation status
                    donation.PaymentStatus = CommonConstants.TransactionStatus.Success;
                    await _db.SaveChangesAsync();
                    
                    // Tell the actor system about the completed donation
                    _logger.LogInformation($"Sending message to DonationActor for reference: {reference}");
                    TopLevelActor.DonationActor.Tell(new DonationCompletedMessage(
                        donation.TransactionReference,
                        donation.Amount,
                        donation.DonorPhone
                    ));
                    
                    await transaction.CommitAsync();
                    _logger.LogInformation($"Transaction committed for reference: {reference}");
                    
                    _apiResponse.IsSuccess = true;
                    _apiResponse.StatusCode = HttpStatusCode.OK;
                    _apiResponse.Message = "Payment verified successfully";
                    return Ok(_apiResponse);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error updating donation after verification for reference: {reference}");
                    await transaction.RollbackAsync();
                    _logger.LogInformation($"Transaction rolled back for reference: {reference}");
                    throw;
                }
            }
            
            _logger.LogWarning($"PayStack verification failed for reference: {reference}. Status: {verification.Status}, Data.Status: {verification.Data?.Status}");
            _apiResponse.IsSuccess = false;
            _apiResponse.StatusCode = HttpStatusCode.BadRequest;
            _apiResponse.Message = $"Payment verification failed. PayStack status: {verification.Data?.Status}";
            return BadRequest(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error verifying payment for reference: {reference}");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong";
            _apiResponse.Errors = new List<string> { ex.Message };
            
            return StatusCode(500, _apiResponse);
        }
    }
    
    /// <summary>
    /// Lists all donations with their status - for debugging
    /// </summary>
    [HttpGet("list")]
    [Authorize(Roles = CommonConstants.Roles.Admin)]
    public async Task<IActionResult> ListDonations()
    {
        try
        {
            _logger.LogInformation("Listing all donations");
            var donations = await _db.Donations
                .OrderByDescending(d => d.DonationDate)
                .Take(50)
                .ToListAsync();
            
            _logger.LogInformation($"Found {donations.Count} donations");
            
            var results = donations.Select(d => new {
                Id = d.Id,
                Reference = d.TransactionReference,
                Amount = d.Amount,
                Currency = d.Currency,
                Status = d.PaymentStatus,
                Date = d.DonationDate,
                Phone = d.DonorPhone != null ? "****" + d.DonorPhone.Substring(Math.Max(0, d.DonorPhone.Length - 4)) : null
            }).ToList();
            
            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Result = results;
            
            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing donations");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong";
            _apiResponse.Errors = new List<string> { ex.Message };
            
            return StatusCode(500, _apiResponse);
        }
    }
    
    /// <summary>
    /// Manually set a donation status - for debugging
    /// </summary>
    [HttpPost("force-status/{reference}")]
    [Authorize(Roles = CommonConstants.Roles.Admin)]
    public async Task<IActionResult> ForceStatus(string reference, [FromQuery] string status)
    {
        try
        {
            _logger.LogInformation($"Manually setting status for donation {reference} to {status}");
            
            var donation = await _db.Donations.FirstOrDefaultAsync(d => d.TransactionReference == reference);
            
            if (donation == null)
            {
                _logger.LogWarning($"Donation not found for reference: {reference}");
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.NotFound;
                _apiResponse.Message = "Donation not found";
                return NotFound(_apiResponse);
            }
            
            donation.PaymentStatus = status;
            await _db.SaveChangesAsync();
            
            _logger.LogInformation($"Status updated for donation {reference}");
            
            // If setting to success, also notify actor system
            if (status.Equals(CommonConstants.TransactionStatus.Success, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"Sending message to DonationActor for reference: {reference}");
                TopLevelActor.DonationActor.Tell(new DonationCompletedMessage(
                    donation.TransactionReference,
                    donation.Amount,
                    donation.DonorPhone
                ));
            }
            
            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Message = "Status updated successfully";
            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating status for donation {reference}");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong";
            _apiResponse.Errors = new List<string> { ex.Message };
            
            return StatusCode(500, _apiResponse);
        }
    }
    
    /// <summary>
    /// Get donation statistics
    /// </summary>
    [HttpGet("statistics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatistics()
    {
        try
        {
            _logger.LogInformation("Fetching donation statistics");
            var stats = await _db.DonationStatistics.FindAsync(1);
            
            if (stats == null)
            {
                _logger.LogInformation("No statistics record found, creating default");
                stats = new DonationStatistics
                {
                    TotalDonations = 0,
                    TotalDonors = 0,
                    MealsServed = 0,
                    LastUpdated = DateTime.UtcNow
                };
            }
            
            _logger.LogInformation($"Statistics: Total Donations: {stats.TotalDonations}, Total Donors: {stats.TotalDonors}, Meals Served: {stats.MealsServed}");
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