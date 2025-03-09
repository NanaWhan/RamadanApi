using System.Net;
using Akka.Actor;
using Microsoft.AspNetCore.Mvc;
using RamadanReliefAPI.Actors;
using RamadanReliefAPI.Models;

namespace RamadanReliefAPI.Controllers;

[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
    private readonly ILogger<SystemController> _logger;
    private ApiResponse _apiResponse;

    public SystemController(ILogger<SystemController> logger)
    {
        _logger = logger;
        _apiResponse = new ApiResponse();
    }

    [HttpGet("actor-health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult CheckActorHealth()
    {
        try
        {
            _logger.LogInformation("Checking actor system health");
            
            var actorStatus = new Dictionary<string, bool>
            {
                ["MainActor"] = TopLevelActor.MainActor != ActorRefs.Nobody,
                ["DonationActor"] = TopLevelActor.DonationActor != ActorRefs.Nobody,
                ["VolunteerActor"] = TopLevelActor.VolunteerActor != ActorRefs.Nobody,
                ["PartnerActor"] = TopLevelActor.PartnerActor != ActorRefs.Nobody
            };
            
            var allHealthy = actorStatus.Values.All(v => v);
            
            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Message = allHealthy ? "Actor system is healthy" : "Some actors are not initialized";
            _apiResponse.Result = actorStatus;
            
            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking actor system health");
            _apiResponse.IsSuccess = false;
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Error checking actor system health";
            _apiResponse.Errors = new List<string> { ex.Message };
            
            return StatusCode(500, _apiResponse);
        }
    }

    [HttpPost("test-sms")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> TestSms([FromBody] TestSmsRequest request)
    {
        try
        {
            _logger.LogInformation($"Sending test SMS to {request.PhoneNumber}");
            
            using var httpClient = new HttpClient();
            
            var message = "This is a test message from Ramadan Relief API";
            var smsKey = "2nwkmCOVenT5pV0BZMFFiDnsn";
            var sender = "RamRelief25";
            
            var requestUrl = $"https://apps.mnotify.net/smsapi?key={smsKey}&to={request.PhoneNumber}&msg={Uri.EscapeDataString(message)}&sender_id={sender}";
            
            var response = await httpClient.GetAsync(requestUrl);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            _apiResponse.IsSuccess = response.IsSuccessStatusCode;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Message = response.IsSuccessStatusCode 
                ? "Test SMS sent successfully" 
                : "Failed to send test SMS";
            _apiResponse.Result = new 
            { 
                StatusCode = response.StatusCode,
                Response = responseContent
            };
            
            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending test SMS");
            _apiResponse.IsSuccess = false;
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Error sending test SMS";
            _apiResponse.Errors = new List<string> { ex.Message };
            
            return StatusCode(500, _apiResponse);
        }
    }
}

public class TestSmsRequest
{
    public string PhoneNumber { get; set; }
}