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
using RamadanReliefAPI.Models.Dtos;
using RamadanReliefAPI.Models.Dtos.Partners;
using RamadanReliefAPI.Models.Dtos.Partners;

namespace RamadanReliefAPI.Controllers;

[ApiController]
[Route("api/partners")]
public class PartnersController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<PartnersController> _logger;
    private ApiResponse _apiResponse;

    public PartnersController(
        ApplicationDbContext db,
        ILogger<PartnersController> logger)
    {
        _db = db;
        _logger = logger;
        _apiResponse = new ApiResponse();
    }

    /// <summary>
    /// Register a new partner organization
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RegisterPartner([FromBody] PartnerRequest partnerRequest)
    {
        try
        {
            _logger.LogInformation($"Registering new partner: {partnerRequest.OrganizationName}");

            // Create and save the partner record
            var partner = new Partners
            {
                OrganizationName = partnerRequest.OrganizationName,
                ContactPerson = partnerRequest.ContactPerson,
                Email = partnerRequest.Email,
                Phone = partnerRequest.Phone,
                City = partnerRequest.City,
                Message = partnerRequest.Message,
                RegistrationDate = DateTime.UtcNow,
                IsActive = true
            };

            await _db.Partners.AddAsync(partner);
            await _db.SaveChangesAsync();
            _logger.LogInformation($"Partner record created in database with id: {partner.Id}");

            // Notify the actor system
            _logger.LogInformation($"Sending message to PartnerActor for partner: {partner.Id}");
            TopLevelActor.PartnerActor.Tell(new PartnerRegisteredMessage(
                partner.Id,
                partner.OrganizationName,
                partner.ContactPerson,
                partner.Email,
                partner.Phone
            ));

            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Message = "Partner registration successful";
            _apiResponse.Result = new
            {
                PartnerId = partner.Id,
                OrganizationName = partner.OrganizationName,
                ContactPerson = partner.ContactPerson,
                Email = partner.Email
            };

            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing partner registration");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong while processing your registration";
            _apiResponse.Errors = new List<string> { ex.Message };

            return StatusCode(500, _apiResponse);
        }
    }

    /// <summary>
    /// Get all partners
    /// </summary>
    [HttpGet("list")]
    [Authorize(Roles = CommonConstants.Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllPartners()
    {
        try
        {
            _logger.LogInformation("Fetching all partners");
            var partners = await _db.Partners.ToListAsync();

            var partnerResponses = partners.Select(p => new PartnerResponse
            {
                Id = p.Id,
                OrganizationName = p.OrganizationName,
                ContactPerson = p.ContactPerson,
                Email = p.Email,
                Phone = p.Phone,
                City = p.City,
                Message = p.Message,
                RegistrationDate = p.RegistrationDate,
                IsActive = p.IsActive
            }).ToList();

            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Result = partnerResponses;

            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching partners");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong while fetching partners";
            _apiResponse.Errors = new List<string> { ex.Message };

            return StatusCode(500, _apiResponse);
        }
    }

    /// <summary>
    /// Get partner by ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Roles = CommonConstants.Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPartnerById(Guid id)
    {
        try
        {
            _logger.LogInformation($"Fetching partner with ID: {id}");
            var partner = await _db.Partners.FindAsync(id);

            if (partner == null)
            {
                _logger.LogWarning($"Partner with ID {id} not found");
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.NotFound;
                _apiResponse.Message = "Partner not found";
                return NotFound(_apiResponse);
            }

            var partnerResponse = new PartnerResponse
            {
                Id = partner.Id,
                OrganizationName = partner.OrganizationName,
                ContactPerson = partner.ContactPerson,
                Email = partner.Email,
                Phone = partner.Phone,
                City = partner.City,
                Message = partner.Message,
                RegistrationDate = partner.RegistrationDate,
                IsActive = partner.IsActive
            };

            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Result = partnerResponse;

            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching partner with ID: {id}");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong while fetching the partner";
            _apiResponse.Errors = new List<string> { ex.Message };

            return StatusCode(500, _apiResponse);
        }
    }

    /// <summary>
    /// Update partner status (activate/deactivate)
    /// </summary>
    [HttpPatch("{id}/status")]
    [Authorize(Roles = CommonConstants.Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdatePartnerStatus(Guid id, [FromQuery] bool isActive)
    {
        try
        {
            _logger.LogInformation($"Updating status for partner ID: {id} to {isActive}");
            var partner = await _db.Partners.FindAsync(id);

            if (partner == null)
            {
                _logger.LogWarning($"Partner with ID {id} not found");
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.NotFound;
                _apiResponse.Message = "Partner not found";
                return NotFound(_apiResponse);
            }

            partner.IsActive = isActive;
            await _db.SaveChangesAsync();

            _logger.LogInformation($"Partner status updated successfully for ID: {id}");
            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Message = $"Partner status updated to {(isActive ? "active" : "inactive")}";

            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating status for partner ID: {id}");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong while updating partner status";
            _apiResponse.Errors = new List<string> { ex.Message };

            return StatusCode(500, _apiResponse);
        }
    }

    /// <summary>
    /// Send message to all partners
    /// </summary>
    [HttpPost("send-message")]
    [Authorize(Roles = CommonConstants.Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendMessageToPartners([FromBody] SendMessageRequest request)
    {
        try
        {
            _logger.LogInformation("Sending message to all partners");

            // Get all active partners with phone numbers
            var partners = await _db.Partners
                .Where(p => p.IsActive && !string.IsNullOrEmpty(p.Phone))
                .ToListAsync();

            if (!partners.Any())
            {
                _logger.LogWarning("No active partners found with phone numbers");
                _apiResponse.IsSuccess = true;
                _apiResponse.StatusCode = HttpStatusCode.OK;
                _apiResponse.Message = "No active partners found with phone numbers";
                return Ok(_apiResponse);
            }

            // Collect all phone numbers
            var phoneNumbers = partners.Select(p => p.Phone).ToList();

            // Create service scope for SMS sending
            using var scope = HttpContext.RequestServices.CreateScope();
            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

            var smsKey = "2nwkmCOVenT5pV0BZMFFiDnsn";
            var sender = "RamRelief25";
            var message = request.Message;

// Join all phone numbers with a comma for bulk sending
            var recipientsList = string.Join(",", phoneNumbers);

            _logger.LogInformation($"Sending bulk SMS to {phoneNumbers.Count} partners");
            var mnotifyResponse = await httpClient.GetAsync(
                $"https://apps.mnotify.net/smsapi?key={smsKey}&to={recipientsList}&msg={Uri.EscapeDataString(message)}&sender_id={sender}"
            );

            if (mnotifyResponse.IsSuccessStatusCode)
            {
                var result = await mnotifyResponse.Content.ReadAsStringAsync();
                _logger.LogInformation(
                    $"SMS notification sent successfully to {partners.Count} partners. Result: {result}");

                _apiResponse.IsSuccess = true;
                _apiResponse.StatusCode = HttpStatusCode.OK;
                _apiResponse.Message = $"Message sent successfully to {partners.Count} partners";
                return Ok(_apiResponse);
            }
            else
            {
                var errorContent = await mnotifyResponse.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    $"Failed to send SMS to partners. Status: {mnotifyResponse.StatusCode}, Error: {errorContent}");

                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.BadRequest;
                _apiResponse.Message = "Failed to send message to partners";
                _apiResponse.Errors = new List<string> { errorContent };
                return BadRequest(_apiResponse);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to partners");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong while sending message to partners";
            _apiResponse.Errors = new List<string> { ex.Message };

            return StatusCode(500, _apiResponse);
        }
    }

    /// <summary>
    /// Send message to a specific partner
    /// </summary>
    [HttpPost("{id}/send-message")]
    [Authorize(Roles = CommonConstants.Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendMessageToPartner(Guid id, [FromBody] SendMessageRequest request)
    {
        try
        {
            _logger.LogInformation($"Sending message to partner ID: {id}");

            var partner = await _db.Partners.FindAsync(id);

            if (partner == null)
            {
                _logger.LogWarning($"Partner with ID {id} not found");
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.NotFound;
                _apiResponse.Message = "Partner not found";
                return NotFound(_apiResponse);
            }

            if (!partner.IsActive)
            {
                _logger.LogWarning($"Partner with ID {id} is not active");
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.BadRequest;
                _apiResponse.Message = "Cannot send message to inactive partner";
                return BadRequest(_apiResponse);
            }

            if (string.IsNullOrEmpty(partner.Phone))
            {
                _logger.LogWarning($"Partner with ID {id} has no phone number");
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.BadRequest;
                _apiResponse.Message = "Partner has no phone number";
                return BadRequest(_apiResponse);
            }

            // Create service scope for SMS sending
            using var scope = HttpContext.RequestServices.CreateScope();
            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

            var smsKey = "2nwkmCOVenT5pV0BZMFFiDnsn";
            var sender = "RamRelief25";
            var message = request.Message;

            _logger.LogInformation($"Sending SMS to partner: {partner.OrganizationName}");
            var mnotifyResponse = await httpClient.GetAsync(
                $"https://apps.mnotify.net/smsapi?key={smsKey}&to={partner.Phone}&msg={Uri.EscapeDataString(message)}&sender_id={sender}"
            );

            if (mnotifyResponse.IsSuccessStatusCode)
            {
                var result = await mnotifyResponse.Content.ReadAsStringAsync();
                _logger.LogInformation($"SMS notification sent successfully to partner ID: {id}. Result: {result}");

                _apiResponse.IsSuccess = true;
                _apiResponse.StatusCode = HttpStatusCode.OK;
                _apiResponse.Message = "Message sent successfully to partner";
                return Ok(_apiResponse);
            }
            else
            {
                var errorContent = await mnotifyResponse.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    $"Failed to send SMS to partner ID: {id}. Status: {mnotifyResponse.StatusCode}, Error: {errorContent}");

                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.BadRequest;
                _apiResponse.Message = "Failed to send message to partner";
                _apiResponse.Errors = new List<string> { errorContent };
                return BadRequest(_apiResponse);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error sending message to partner ID: {id}");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong while sending message to partner";
            _apiResponse.Errors = new List<string> { ex.Message };

            return StatusCode(500, _apiResponse);
        }
    }
}