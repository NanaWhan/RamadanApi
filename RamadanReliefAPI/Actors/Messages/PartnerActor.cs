using System.Text;
using Akka.Actor;
using Microsoft.EntityFrameworkCore;
using RamadanReliefAPI.Actors.Messages;
using RamadanReliefAPI.Data;
using RamadanReliefAPI.Models.DomainModels;

namespace RamadanReliefAPI.Actors;

public class PartnerActor : BaseActor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PartnerActor> _logger;
    private readonly ApplicationDbContext _db;

    public PartnerActor(
        IServiceProvider serviceProvider,
        ILogger<PartnerActor> logger,
        ApplicationDbContext db
    )
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _db = db;

        _logger.LogInformation("PartnerActor initialized and ready to receive messages");
        ReceiveAsync<PartnerRegisteredMessage>(ProcessPartnerRegistration);
    }

    private async Task ProcessPartnerRegistration(PartnerRegisteredMessage message)
    {
        _logger.LogInformation($"PartnerActor received registration message for: {message.Email}");

        try
        {
            // Find the partner to ensure they exist
            var partner = await _db.Partners
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == message.PartnerId);

            if (partner == null)
            {
                _logger.LogWarning($"PartnerActor: Partner not found in database: {message.PartnerId}");
                return;
            }

            _logger.LogInformation(
                $"PartnerActor: Found partner. Organization: {partner.OrganizationName}, Contact: {partner.ContactPerson}");

            // Send welcome message
            await SendWelcomeSms(partner);

            _logger.LogInformation($"PartnerActor: Successfully processed partner registration: {partner.Id}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"PartnerActor: Error processing partner registration: {message.PartnerId}");
        }
    }

    private async Task SendWelcomeSms(Partners partner)
    {
        _logger.LogInformation($"PartnerActor: Preparing to send welcome SMS for partner: {partner.Id}");

        // Skip if no phone number is provided
        if (string.IsNullOrEmpty(partner.Phone))
        {
            _logger.LogInformation(
                $"PartnerActor: No phone number provided for partner: {partner.Id}, skipping SMS notification");
            return;
        }

        try
        {
            _logger.LogInformation($"PartnerActor: Creating service scope for SMS sending");
            using var scope = _serviceProvider.CreateScope();
            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

            // Ensure the phone number is properly formatted
            var cleanedPhoneNumber = partner.Phone.Trim().Replace(" ", "");
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
                $"Thank you for partnering with Ramadan Relief, {partner.OrganizationName}! We value your support and will be in touch soon to discuss our collaboration further.";
            _logger.LogInformation($"PartnerActor: SMS message: {message}");

            var smsKey = "2nwkmCOVenT5pV0BZMFFiDnsn";
            var sender = "RamRelief25";

            // Properly encode the message for URL transmission
            var encodedMessage = Uri.EscapeDataString(message);
            var requestUrl =
                $"https://apps.mnotify.net/smsapi?key={smsKey}&to={cleanedPhoneNumber}&msg={encodedMessage}&sender_id={sender}";

            _logger.LogInformation($"PartnerActor: Sending SMS request to mNotify API: {requestUrl}");
            var mnotifyResponse = await httpClient.GetAsync(requestUrl);

            var responseContent = await mnotifyResponse.Content.ReadAsStringAsync();
            _logger.LogInformation(
                $"PartnerActor: mNotify API response: {mnotifyResponse.StatusCode} - {responseContent}");

            if (mnotifyResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    $"PartnerActor: SMS notification sent successfully for {partner.Id}. Result: {responseContent}");
            }
            else
            {
                _logger.LogWarning(
                    $"PartnerActor: Failed to send SMS for {partner.Id}. Status: {mnotifyResponse.StatusCode}, Error: {responseContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"PartnerActor: Error sending welcome SMS for partner: {partner.Id}");
            // We don't rethrow here as SMS failure shouldn't stop the entire process
        }
    }
}