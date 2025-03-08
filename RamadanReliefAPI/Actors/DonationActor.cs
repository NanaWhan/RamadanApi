using System.Text;
using Akka.Actor;
using Microsoft.EntityFrameworkCore;
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

        _logger.LogInformation("DonationActor initialized and ready to receive messages");
        ReceiveAsync<DonationCompletedMessage>(ProcessCompletedDonation);
    }

    private async Task ProcessCompletedDonation(DonationCompletedMessage message)
    {
        _logger.LogInformation($"DonationActor received message for transaction reference: {message.TransactionReference}");
        
        try
        {
            _logger.LogInformation($"Looking up donation in database: {message.TransactionReference}");
            // Find the donation with tracking disabled to avoid Entity Framework tracking conflicts
            var donation = await _db.Donations
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.TransactionReference == message.TransactionReference);
            
            if (donation == null)
            {
                _logger.LogWarning($"DonationActor: Donation not found in database: {message.TransactionReference}");
                return;
            }
            
            _logger.LogInformation($"DonationActor: Found donation. Status: {donation.PaymentStatus}, Amount: {donation.Amount}, DonorPhone: {donation.DonorPhone ?? "None"}");
            
            if (donation.PaymentStatus != CommonConstants.TransactionStatus.Success)
            {
                _logger.LogWarning($"DonationActor: Donation status is not SUCCESS (it's {donation.PaymentStatus}). Will not process further.");
                
                // Double-check if we need to update the status
                var donationToUpdate = await _db.Donations.FirstOrDefaultAsync(d => d.TransactionReference == message.TransactionReference);
                if (donationToUpdate != null && donationToUpdate.PaymentStatus != CommonConstants.TransactionStatus.Success)
                {
                    _logger.LogInformation($"DonationActor: Updating donation status to SUCCESS for {message.TransactionReference}");
                    donationToUpdate.PaymentStatus = CommonConstants.TransactionStatus.Success;
                    await _db.SaveChangesAsync();
                    _logger.LogInformation($"DonationActor: Successfully updated donation status");
                }
                
                return;
            }
            
            _logger.LogInformation($"DonationActor: Processing donation {message.TransactionReference} with parallel tasks");
            
            // Execute these tasks in parallel
            var sendSmsTask = SendThankYouSms(donation);
            var updateStatsTask = UpdateDonationStatistics(donation.Amount);
            
            // Wait for both tasks to complete
            await Task.WhenAll(sendSmsTask, updateStatsTask);
            
            _logger.LogInformation($"DonationActor: Successfully processed donation: {message.TransactionReference}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"DonationActor: Error processing completed donation: {message.TransactionReference}");
            // Consider implementing a retry mechanism here for critical failures
        }
    }

    private async Task SendThankYouSms(Donation donation)
    {
        _logger.LogInformation($"DonationActor: Preparing to send thank you SMS for {donation.TransactionReference}");
        
        // Skip if no phone number is provided
        if (string.IsNullOrEmpty(donation.DonorPhone))
        {
            _logger.LogInformation($"DonationActor: No phone number provided for donation: {donation.TransactionReference}, skipping SMS notification");
            return;
        }

        try
        {
            _logger.LogInformation($"DonationActor: Creating service scope for SMS sending");
            using var scope = _serviceProvider.CreateScope();
            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();
            
            var message = $"Thank you for your donation of {donation.Amount} {donation.Currency} to Ramadan Relief. Your generosity will help provide {CalculateMeals(donation.Amount)} meals for those in need during the holy month.";
            _logger.LogInformation($"DonationActor: SMS message: {message}");
            
            _logger.LogInformation($"DonationActor: Creating SMS request body for {donation.DonorPhone}");
            var postBody = new SmsBody()
            {
                schedule_date = "",
                is_schedule = "false",
                message = message,
                sender = "Ramadan",
                recipient = new List<string>() { donation.DonorPhone }
            };
            
            _logger.LogInformation($"DonationActor: Serializing SMS request body");
            HttpContent body = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(postBody),
                Encoding.UTF8,
                "application/json"
            );

            var smsKey = "2nwkmCOVenT5pV0BZMFFiDnsn";
            _logger.LogInformation($"DonationActor: Sending SMS request to mNotify API");
            var mnotifyResponse = await httpClient.PostAsync(
                $"https://api.mnotify.com/api/sms/quick?key={smsKey}",
                body
            );

            if (mnotifyResponse.IsSuccessStatusCode)
            {
                var result = await mnotifyResponse.Content.ReadAsStringAsync();
                _logger.LogInformation($"DonationActor: SMS notification sent successfully for {donation.TransactionReference}. Result: {result}");
            }
            else
            {
                var errorContent = await mnotifyResponse.Content.ReadAsStringAsync();
                _logger.LogWarning($"DonationActor: Failed to send SMS for {donation.TransactionReference}. Status: {mnotifyResponse.StatusCode}, Error: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"DonationActor: Error sending thank you SMS for donation: {donation.TransactionReference}");
            // We don't rethrow here as SMS failure shouldn't stop the entire process
        }
    }

    private async Task UpdateDonationStatistics(decimal amount)
    {
        _logger.LogInformation($"DonationActor: Updating donation statistics for amount: {amount}");
        
        try
        {
            // Use a transaction to ensure data consistency
            _logger.LogInformation("DonationActor: Beginning database transaction for statistics update");
            using var transaction = await _db.Database.BeginTransactionAsync();
            
            try
            {
                // Load statistics with update lock to prevent concurrency issues
                _logger.LogInformation("DonationActor: Querying statistics record with FOR UPDATE lock");
                var stats = await _db.DonationStatistics
                    .FromSqlRaw("SELECT * FROM \"DonationStatistics\" WHERE \"Id\" = 1 FOR UPDATE")
                    .FirstOrDefaultAsync();
                
                if (stats == null)
                {
                    _logger.LogInformation("DonationActor: No statistics record found, creating new one");
                    stats = new DonationStatistics
                    {
                        Id = 1,
                        TotalDonations = amount,
                        TotalDonors = 1,
                        MealsServed = CalculateMeals(amount),
                        LastUpdated = DateTime.UtcNow
                    };
                    await _db.DonationStatistics.AddAsync(stats);
                    _logger.LogInformation($"DonationActor: Created new statistics record with initial amount: {amount}");
                }
                else
                {
                    _logger.LogInformation($"DonationActor: Updating existing statistics. Current values - Total: {stats.TotalDonations}, Donors: {stats.TotalDonors}, Meals: {stats.MealsServed}");
                    stats.TotalDonations += amount;
                    stats.TotalDonors += 1;
                    stats.MealsServed += CalculateMeals(amount);
                    stats.LastUpdated = DateTime.UtcNow;
                    _logger.LogInformation($"DonationActor: Updated statistics. New values - Total: {stats.TotalDonations}, Donors: {stats.TotalDonors}, Meals: {stats.MealsServed}");
                }
                
                _logger.LogInformation("DonationActor: Saving statistics changes to database");
                await _db.SaveChangesAsync();
                
                _logger.LogInformation("DonationActor: Committing transaction");
                await transaction.CommitAsync();
                
                _logger.LogInformation("DonationActor: Statistics update completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DonationActor: Error during statistics update, rolling back transaction");
                await transaction.RollbackAsync();
                throw new Exception("Failed to update donation statistics", ex);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DonationActor: Error updating donation statistics");
            // Consider implementing a retry mechanism or event sourcing pattern for critical statistics updates
        }
    }
    
    private int CalculateMeals(decimal amount)
    {
        // Assuming 1 meal costs 5 GHS
        _logger.LogInformation($"DonationActor: Calculating meals for amount {amount}. (Rate: 1 meal = 5 GHS)");
        int meals = (int)(amount / 5);
        _logger.LogInformation($"DonationActor: Calculated {meals} meals for amount {amount}");
        return meals;
    }
}