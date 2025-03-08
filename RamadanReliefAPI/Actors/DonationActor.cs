using System.Text;
using Akka.Actor;
using RamadanReliefAPI.Actors.Messages;
using RamadanReliefAPI.Data;
using RamadanReliefAPI.Extensions;
using RamadanReliefAPI.Models.DomainModels;

namespace RamadanReliefAPI.Actors;

public class DonationActor : BaseActor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DonationActor> _logger;
    private readonly ApplicationDbContext _db;

    public DonationActor(
        IServiceProvider serviceProvider,
        ILogger<DonationActor> logger,
        ApplicationDbContext db
    )
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _db = db;

        ReceiveAsync<DonationCompletedMessage>(ProcessCompletedDonation);
    }

    private async Task ProcessCompletedDonation(DonationCompletedMessage message)
    {
        try
        {
            // Find the donation
            var donation = await _db.Donations.FindAsync(message.TransactionReference);
            
            if (donation == null || donation.PaymentStatus != CommonConstants.TransactionStatus.Success)
            {
                _logger.LogWarning($"Donation not found or not successful: {message.TransactionReference}");
                return;
            }
            
            // Send thank you message if we have contact information
            if (!string.IsNullOrEmpty(donation.DonorPhone))
            {
                using var scope = _serviceProvider.CreateScope();
                var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();
                
                // Similar to your NewsLetter SMS sending logic
                var postBody = new SmsBody()
                {
                    schedule_date = "",
                    is_schedule = "false",
                    message = $"Thank you for your donation of {donation.Amount} {donation.Currency} to Ramadan Relief. Your generosity will help provide meals for those in need during the holy month.",
                    sender = "Ramadan",
                    recipient = new List<string>() { donation.DonorPhone }
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
                _logger.LogInformation($"SMS notification result: {result}");
            }
            
            // Update the donation statistics
            await UpdateDonationStatistics(donation.Amount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing completed donation");
        }
    }

    private async Task UpdateDonationStatistics(decimal amount)
    {
        try
        {
            var stats = await _db.DonationStatistics.FindAsync(1);
            
            if (stats == null)
            {
                stats = new DonationStatistics
                {
                    TotalDonations = amount,
                    TotalDonors = 1,
                    MealsServed = CalculateMeals(amount),
                    LastUpdated = DateTime.UtcNow
                };
                await _db.DonationStatistics.AddAsync(stats);
            }
            else
            {
                stats.TotalDonations += amount;
                stats.TotalDonors += 1;
                stats.MealsServed += CalculateMeals(amount);
                stats.LastUpdated = DateTime.UtcNow;
            }
            
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating donation statistics");
        }
    }
    
    private int CalculateMeals(decimal amount)
    {
        // Assuming 1 meal costs 5 GHS
        return (int)(amount / 5);
    }
}