using System.Text;
using Akka.Actor;
using Microsoft.EntityFrameworkCore;
using RamadanReliefAPI.Actors.Messages;
using RamadanReliefAPI.Data;
using RamadanReliefAPI.Models.DomainModels;

namespace RamadanReliefAPI.Actors;

public class VolunteerActor : BaseActor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VolunteerActor> _logger;
    private readonly ApplicationDbContext _db;

    public VolunteerActor(
        IServiceProvider serviceProvider,
        ILogger<VolunteerActor> logger,
        ApplicationDbContext db
    )
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _db = db;

        _logger.LogInformation("VolunteerActor initialized and ready to receive messages");
        ReceiveAsync<VolunteerRegisteredMessage>(ProcessVolunteerRegistration);
    }

    private async Task ProcessVolunteerRegistration(VolunteerRegisteredMessage message)
    {
        _logger.LogInformation($"VolunteerActor received registration message for: {message.Email}");
        
        try
        {
            // Find the volunteer to ensure they exist
            var volunteer = await _db.Volunteers
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == message.VolunteerId);
                
            if (volunteer == null)
            {
                _logger.LogWarning($"VolunteerActor: Volunteer not found in database: {message.VolunteerId}");
                return;
            }
            
            _logger.LogInformation($"VolunteerActor: Found volunteer. Name: {volunteer.Name}, Email: {volunteer.Email}");
            
            // Send welcome message
            await SendWelcomeSms(volunteer);
            
            _logger.LogInformation($"VolunteerActor: Successfully processed volunteer registration: {volunteer.Id}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"VolunteerActor: Error processing volunteer registration: {message.VolunteerId}");
        }
    }

    private async Task SendWelcomeSms(Volunteer volunteer)
    {
        _logger.LogInformation($"VolunteerActor: Preparing to send welcome SMS for volunteer: {volunteer.Id}");
        
        // Skip if no phone number is provided
        if (string.IsNullOrEmpty(volunteer.Phone))
        {
            _logger.LogInformation($"VolunteerActor: No phone number provided for volunteer: {volunteer.Id}, skipping SMS notification");
            return;
        }

        try
        {
            _logger.LogInformation($"VolunteerActor: Creating service scope for SMS sending");
            using var scope = _serviceProvider.CreateScope();
            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();
            
            var message = $"Thank you for volunteering with Ramadan Relief! Your support is greatly appreciated. We will contact you soon with more information about upcoming volunteer opportunities.";
            _logger.LogInformation($"VolunteerActor: SMS message: {message}");
            
            _logger.LogInformation($"VolunteerActor: Creating SMS request body for {volunteer.Phone}");
            var postBody = new SmsBody()
            {
                schedule_date = "",
                is_schedule = "false",
                message = message,
                sender = "RamRelief25",
                recipient = new List<string>() { volunteer.Phone }
            };
            
            _logger.LogInformation($"VolunteerActor: Serializing SMS request body");
            HttpContent body = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(postBody),
                Encoding.UTF8,
                "application/json"
            );

            var smsKey = "2nwkmCOVenT5pV0BZMFFiDnsn";
            _logger.LogInformation($"VolunteerActor: Sending SMS request to mNotify API");
            var mnotifyResponse = await httpClient.GetAsync(
                $"https://apps.mnotify.net/smsapi?key={smsKey}&to={volunteer.Phone}&msg={Uri.EscapeDataString(message)}&sender_id=RamRelief25"
            );

            if (mnotifyResponse.IsSuccessStatusCode)
            {
                var result = await mnotifyResponse.Content.ReadAsStringAsync();
                _logger.LogInformation($"VolunteerActor: SMS notification sent successfully for {volunteer.Id}. Result: {result}");
            }
            else
            {
                var errorContent = await mnotifyResponse.Content.ReadAsStringAsync();
                _logger.LogWarning($"VolunteerActor: Failed to send SMS for {volunteer.Id}. Status: {mnotifyResponse.StatusCode}, Error: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"VolunteerActor: Error sending welcome SMS for volunteer: {volunteer.Id}");
        }
    }
}