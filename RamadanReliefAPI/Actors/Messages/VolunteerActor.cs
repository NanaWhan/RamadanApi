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

            _logger.LogInformation(
                $"VolunteerActor: Found volunteer. Name: {volunteer.Name}, Email: {volunteer.Email}");

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
            _logger.LogInformation(
                $"VolunteerActor: No phone number provided for volunteer: {volunteer.Id}, skipping SMS notification");
            return;
        }

        try
        {
            _logger.LogInformation($"VolunteerActor: Creating service scope for SMS sending");
            using var scope = _serviceProvider.CreateScope();
            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

            // Ensure the phone number is properly formatted
            var cleanedPhoneNumber = volunteer.Phone.Trim().Replace(" ", "");
            if (!cleanedPhoneNumber.StartsWith("+"))
            {
                // Assume Ghana number if no country code provided
                if (cleanedPhoneNumber.StartsWith("0"))
                {
                    cleanedPhoneNumber = "+233" + cleanedPhoneNumber.Substring(1);
                }
                else
                {
                    cleanedPhoneNumber = "+233" + cleanedPhoneNumber;
                }
            }

            var message =
                $"Thank you for volunteering with Ramadan Relief! Your support is greatly appreciated. We will contact you soon with more information about upcoming volunteer opportunities.";
            _logger.LogInformation($"VolunteerActor: SMS message: {message}");

            var smsKey = "2nwkmCOVenT5pV0BZMFFiDnsn";
            var sender = "RamRelief25";

            // Properly encode the message for URL transmission
            var encodedMessage = Uri.EscapeDataString(message);
            var requestUrl =
                $"https://apps.mnotify.net/smsapi?key={smsKey}&to={cleanedPhoneNumber}&msg={encodedMessage}&sender_id={sender}";

            _logger.LogInformation($"VolunteerActor: Sending SMS request to mNotify API: {requestUrl}");
            var mnotifyResponse = await httpClient.GetAsync(requestUrl);

            var responseContent = await mnotifyResponse.Content.ReadAsStringAsync();
            _logger.LogInformation(
                $"VolunteerActor: mNotify API response: {mnotifyResponse.StatusCode} - {responseContent}");

            if (mnotifyResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    $"VolunteerActor: SMS notification sent successfully for {volunteer.Id}. Result: {responseContent}");
            }
            else
            {
                _logger.LogWarning(
                    $"VolunteerActor: Failed to send SMS for {volunteer.Id}. Status: {mnotifyResponse.StatusCode}, Error: {responseContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"VolunteerActor: Error sending welcome SMS for volunteer: {volunteer.Id}");
            // We don't rethrow here as SMS failure shouldn't stop the entire process
        }
    }
}