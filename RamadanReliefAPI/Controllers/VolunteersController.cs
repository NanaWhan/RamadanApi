using System.Net;
using Akka.Actor;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RamadanReliefAPI.Actors;
using RamadanReliefAPI.Actors.Messages;
using RamadanReliefAPI.Data;
using RamadanReliefAPI.Models;
using RamadanReliefAPI.Models.DomainModels;
using RamadanReliefAPI.Models.Dtos;
using RamadanReliefAPI.Models.Dtos.Volunteer;

namespace RamadanReliefAPI.Controllers;

[ApiController]
[Route("api/volunteers")]
public class VolunteersController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<VolunteersController> _logger;
    private ApiResponse _apiResponse;

    public VolunteersController(
        ApplicationDbContext db,
        ILogger<VolunteersController> logger)
    {
        _db = db;
        _logger = logger;
        _apiResponse = new ApiResponse();
    }

    /// <summary>
    /// Register a new volunteer
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RegisterVolunteer([FromBody] VolunteerRequest volunteerRequest)
    {
        try
        {
            _logger.LogInformation($"Registering new volunteer: {volunteerRequest.Name}");

            // Create and save the volunteer record
            var volunteer = new Volunteer
            {
                Name = volunteerRequest.Name,
                Email = volunteerRequest.Email,
                Phone = volunteerRequest.Phone,
                City = volunteerRequest.City,
                Message = volunteerRequest.Message,
                RegistrationDate = DateTime.UtcNow,
                IsActive = true,
            };

            await _db.Volunteers.AddAsync(volunteer);
            await _db.SaveChangesAsync();
            _logger.LogInformation($"Volunteer record created in database with id: {volunteer.Id}");

            // Notify the actor system
            _logger.LogInformation($"Sending message to VolunteerActor for volunteer: {volunteer.Id}");
            TopLevelActor.VolunteerActor.Tell(new VolunteerRegisteredMessage(
                volunteer.Id,
                volunteer.Name,
                volunteer.Email,
                volunteer.Phone
            ));

            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Message = "Volunteer registration successful";
            _apiResponse.Result = new
            {
                VolunteerId = volunteer.Id,
                Name = volunteer.Name,
                Email = volunteer.Email
            };

            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing volunteer registration");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong while processing your registration";
            _apiResponse.Errors = new List<string> { ex.Message };

            return StatusCode(500, _apiResponse);
        }
    }

    /// <summary>
    /// Get all volunteers
    /// </summary>
    [HttpGet("list")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllVolunteers()
    {
        try
        {
            _logger.LogInformation("Fetching all volunteers");
            var volunteers = await _db.Volunteers.ToListAsync();

            var volunteerResponses = volunteers.Select(v => new VolunteerResponse
            {
                Id = v.Id,
                Name = v.Name,
                Email = v.Email,
                Phone = v.Phone,
                City = v.City,
                Message = v.Message,
                RegistrationDate = v.RegistrationDate,
                IsActive = v.IsActive
            }).ToList();

            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Result = volunteerResponses;

            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching volunteers");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong while fetching volunteers";
            _apiResponse.Errors = new List<string> { ex.Message };

            return StatusCode(500, _apiResponse);
        }
    }

    /// <summary>
    /// Send message to all volunteers
    /// </summary>
    [HttpPost("send-message")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendMessageToVolunteers([FromBody] SendMessageRequest request)
    {
        try
        {
            _logger.LogInformation("Sending message to all volunteers");

            // Get all active volunteers with phone numbers
            var volunteers = await _db.Volunteers
                .Where(v => v.IsActive && !string.IsNullOrEmpty(v.Phone))
                .ToListAsync();

            if (!volunteers.Any())
            {
                _logger.LogWarning("No active volunteers found with phone numbers");
                _apiResponse.IsSuccess = true;
                _apiResponse.StatusCode = HttpStatusCode.OK;
                _apiResponse.Message = "No active volunteers found with phone numbers";
                return Ok(_apiResponse);
            }

            // Collect all phone numbers
            var phoneNumbers = volunteers.Select(v => v.Phone).ToList();

            // Create service scope for SMS sending
            using var scope = HttpContext.RequestServices.CreateScope();
            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

            // Prepare SMS request
            var postBody = new SmsBody()
            {
                schedule_date = "",
                is_schedule = "false",
                message = request.Message,
                sender = "RR'25",
                recipient = phoneNumbers
            };

            HttpContent body = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(postBody),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var smsKey = "2nwkmCOVenT5pV0BZMFFiDnsn";
            var sender = "RamRelief25";
            var message = request.Message;

// Join all phone numbers with a comma for bulk sending
            var recipientsList = string.Join(",", phoneNumbers);

            _logger.LogInformation($"Sending bulk SMS to {phoneNumbers.Count} volunteers");
            var mnotifyResponse = await httpClient.GetAsync(
                $"https://apps.mnotify.net/smsapi?key={smsKey}&to={recipientsList}&msg={Uri.EscapeDataString(message)}&sender_id={sender}"
            );

            if (mnotifyResponse.IsSuccessStatusCode)
            {
                var result = await mnotifyResponse.Content.ReadAsStringAsync();
                _logger.LogInformation(
                    $"SMS notification sent successfully to {volunteers.Count} volunteers. Result: {result}");

                _apiResponse.IsSuccess = true;
                _apiResponse.StatusCode = HttpStatusCode.OK;
                _apiResponse.Message = $"Message sent successfully to {volunteers.Count} volunteers";
                return Ok(_apiResponse);
            }
            else
            {
                var errorContent = await mnotifyResponse.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    $"Failed to send SMS to volunteers. Status: {mnotifyResponse.StatusCode}, Error: {errorContent}");

                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.BadRequest;
                _apiResponse.Message = "Failed to send message to volunteers";
                _apiResponse.Errors = new List<string> { errorContent };
                return BadRequest(_apiResponse);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to volunteers");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong while sending message to volunteers";
            _apiResponse.Errors = new List<string> { ex.Message };

            return StatusCode(500, _apiResponse);
        }
    }
}