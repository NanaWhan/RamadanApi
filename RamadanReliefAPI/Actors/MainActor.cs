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
            var mnotifyResponse = await httpClient.PostAsync(
                $"https://api.mnotify.com/api/sms/quick?key=" + smsKey,
                body
            );

            var result = await mnotifyResponse.Content.ReadAsStringAsync();

            var data = JsonSerializer.Deserialize<MNotifyResponse>(result);
        }
        catch (Exception e) { }
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