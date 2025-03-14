using System.Text;
using System.Text.Json;
using RamadanReliefAPI.Actors.Messages;
using RamadanReliefAPI.Data;

namespace RamadanReliefAPI.Actors;

public class MainActor : BaseActor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MainActor> _logger;
    private readonly ApplicationDbContext _db;

    public MainActor(
        IServiceProvider serviceProvider,
        ILogger<MainActor> logger,
        ApplicationDbContext db
    )
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _db = db;

        ReceiveAsync<NewsLetterMessage>(DoCreateNewsLetterSms);
        ReceiveAsync<EventRegistrationMessage>(HandleEventRegistration);
        ReceiveAsync<DonationFormSubmittedMessage>(HandleDonationFormSubmission);
    }


    private async Task DoCreateNewsLetterSms(NewsLetterMessage newsLetterMessage)
    {
        try
        {
            using var scoped = _serviceProvider.CreateScope();

            var httpClient = scoped.ServiceProvider.GetRequiredService<HttpClient>();

            var postBody = new SmsBody()
            {
                schedule_date = "",
                is_schedule = "false",
                message =
                    "Welcome to Ramadan Relief. We are glad to have you on board. We will be sending you updates on our activities. Stay tuned.",
                sender = "Ramadan",
                recipient = new List<string>() { newsLetterMessage.PhoneNumber }
            };
            HttpContent body = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(postBody),
                Encoding.UTF8,
                "application/json"
            );

            var smsKey = "2nwkmCOVenT5pV0BZMFFiDnsn";
            var sender = "RamRelief25";
            var message = "Welcome to Ramadan Relief. We are glad to have you on board. We will be sending you updates on our activities. Stay tuned.";
            var mnotifyResponse = await httpClient.GetAsync(
                $"https://apps.mnotify.net/smsapi?key={smsKey}&to={newsLetterMessage.PhoneNumber}&msg={Uri.EscapeDataString(message)}&sender_id={sender}"
            );

            var result = await mnotifyResponse.Content.ReadAsStringAsync();

            var data = JsonSerializer.Deserialize<MNotifyResponse>(result);
        }
        catch (Exception e) { }
    }
    
    private async Task HandleEventRegistration(EventRegistrationMessage message)
{
    try
    {
        _logger.LogInformation($"MainActor: Processing event registration for {message.AttendeeName}");
        
        // Skip if no phone number is provided
        if (string.IsNullOrEmpty(message.PhoneNumber))
        {
            _logger.LogInformation($"MainActor: No phone number provided for event registration, skipping SMS notification");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

        // Format phone number
        var cleanedPhoneNumber = message.PhoneNumber.Trim().Replace(" ", "");
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

        var eventItem = await _db.Events.FindAsync(message.EventId);
        string eventName = eventItem?.Title ?? "Eid Feeding Event";
        string eventLocation = eventItem?.Location ?? "our event";
        
        var messageText = $"Thank you {message.AttendeeName} for registering for the {eventName} in {eventLocation}. We look forward to seeing you and your {message.NumberOfPeople} guests on {eventItem?.EventDate.ToString("MMM dd, yyyy") ?? "the event date"}. Contact us for any questions.";
        _logger.LogInformation($"MainActor: SMS message: {messageText}");

        var smsKey = "2nwkmCOVenT5pV0BZMFFiDnsn";
        var sender = "RamRelief25";
        
        // Properly encode the message
        var encodedMessage = Uri.EscapeDataString(messageText);
        var requestUrl = $"https://apps.mnotify.net/smsapi?key={smsKey}&to={cleanedPhoneNumber}&msg={encodedMessage}&sender_id={sender}";
        
        _logger.LogInformation($"MainActor: Sending SMS request to mNotify API");
        var mnotifyResponse = await httpClient.GetAsync(requestUrl);

        var responseContent = await mnotifyResponse.Content.ReadAsStringAsync();
        _logger.LogInformation($"MainActor: mNotify API response: {mnotifyResponse.StatusCode} - {responseContent}");

        if (mnotifyResponse.IsSuccessStatusCode)
        {
            _logger.LogInformation($"MainActor: SMS notification sent successfully for event registration.");
        }
        else
        {
            _logger.LogWarning($"MainActor: Failed to send SMS for event registration. Status: {mnotifyResponse.StatusCode}, Error: {responseContent}");
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"MainActor: Error sending event registration SMS");
    }
}
    
    private async Task HandleDonationFormSubmission(DonationFormSubmittedMessage message)
{
    try
    {
        _logger.LogInformation($"MainActor: Processing donation form submission from {message.FullName}");
        
        using var scope = _serviceProvider.CreateScope();
        var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

        // Format donor's phone number
        var donorPhoneNumber = FormatPhoneNumber(message.PhoneNumber);
        
        // Send confirmation to donor
        var donorMessage = $"Thank you {message.FullName} for your donation form submission of GHS {message.Amount} to Ramadan Relief. We will contact you soon.";
        await SendSms(httpClient, donorPhoneNumber, donorMessage);
        
        // Send notification to admin
        var adminMessage = $"New donation form submission! {message.FullName} ({message.PhoneNumber}, {message.Email}) has donated GHS {message.Amount}. to the Ramadan Relief Account. Kindly confirm receipt.";
        await SendSms(httpClient, message.AdminPhoneNumber, adminMessage);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"MainActor: Error processing donation form submission");
    }
}

private string FormatPhoneNumber(string phoneNumber)
{
    // Ensure the phone number is properly formatted
    var cleanedPhoneNumber = phoneNumber.Trim().Replace(" ", "");
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
    return cleanedPhoneNumber;
}

private async Task SendSms(HttpClient httpClient, string phoneNumber, string message)
{
    try {
        var smsKey = "2nwkmCOVenT5pV0BZMFFiDnsn";
        var sender = "RamRelief25";
        
        // Properly encode the message
        var encodedMessage = Uri.EscapeDataString(message);
        var requestUrl = $"https://apps.mnotify.net/smsapi?key={smsKey}&to={phoneNumber}&msg={encodedMessage}&sender_id={sender}";
        
        _logger.LogInformation($"MainActor: Sending SMS to {phoneNumber}");
        var mnotifyResponse = await httpClient.GetAsync(requestUrl);

        var responseContent = await mnotifyResponse.Content.ReadAsStringAsync();
        _logger.LogInformation($"MainActor: mNotify API response: {mnotifyResponse.StatusCode} - {responseContent}");

        if (!mnotifyResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning($"MainActor: Failed to send SMS. Status: {mnotifyResponse.StatusCode}, Error: {responseContent}");
        }
    }
    catch (Exception ex) {
        _logger.LogError(ex, $"MainActor: Error sending SMS");
    }
}
}

public class SmsBody
{
    public List<string> recipient { get; set; }
    public string sender { get; set; }
    public string message { get; set; }
    public string is_schedule { get; set; }
    public string schedule_date { get; set; }
}

public class MNotifyResponse
{
    public string status { get; set; }
    public string code { get; set; }
    public string message { get; set; }
    public Summary summary { get; set; }
}

public class Summary
{
    public string _id { get; set; }
    public string type { get; set; }
    public int total_sent { get; set; }
    public int contacts { get; set; }
    public int total_rejected { get; set; }
    public List<string> numbers_sent { get; set; }
    public int credit_used { get; set; }
    public int credit_left { get; set; }
}