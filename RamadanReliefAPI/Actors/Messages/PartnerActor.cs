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
            
            _logger.LogInformation($"PartnerActor: Found partner. Organization: {partner.OrganizationName}, Contact: {partner.ContactPerson}");
            
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
            _logger.LogInformation($"PartnerActor: No phone number provided for partner: {partner.Id}, skipping SMS notification");
            return;
        }

        try
        {
            _logger.LogInformation($"PartnerActor: Creating service scope for SMS sending");
            using var scope = _serviceProvider.CreateScope();
            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();
            
            var message = $"Thank you for partnering with Ramadan Relief, {partner.OrganizationName}! We value your support and will be in touch soon to discuss our collaboration further.";
            _logger.LogInformation($"PartnerActor: SMS message: {message}");
            
            _logger.LogInformation($"PartnerActor: Creating SMS request body for {partner.Phone}");
            var postBody = new SmsBody()
            {
                schedule_date = "",
                is_schedule = "false",
                message = message,
                sender = "RR'25",
                recipient = new List<string>() { partner.Phone }
            };
            
            _logger.LogInformation($"PartnerActor: Serializing SMS request body");
            HttpContent body = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(postBody),
                Encoding.UTF8,
                "application/json"
            );

            var smsKey = "2nwkmCOVenT5pV0BZMFFiDnsn";
            _logger.LogInformation($"PartnerActor: Sending SMS request to mNotify API");
            var mnotifyResponse = await httpClient.GetAsync(
                $"https://apps.mnotify.net/smsapi?key={smsKey}&to={partner.Phone}&msg={Uri.EscapeDataString(message)}&sender_id=RamRelief25"
            );

            if (mnotifyResponse.IsSuccessStatusCode)
            {
                var result = await mnotifyResponse.Content.ReadAsStringAsync();
                _logger.LogInformation($"PartnerActor: SMS notification sent successfully for {partner.Id}. Result: {result}");
            }
            else
            {
                var errorContent = await mnotifyResponse.Content.ReadAsStringAsync();
                _logger.LogWarning($"PartnerActor: Failed to send SMS for {partner.Id}. Status: {mnotifyResponse.StatusCode}, Error: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"PartnerActor: Error sending welcome SMS for partner: {partner.Id}");
        }
    }
}