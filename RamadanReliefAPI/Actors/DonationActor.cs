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
        _logger.LogInformation(
            $"DonationActor received message for transaction reference: {message.TransactionReference}");

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

            _logger.LogInformation(
                $"DonationActor: Found donation. Status: {donation.PaymentStatus}, Amount: {donation.Amount}, DonorPhone: {donation.DonorPhone ?? "None"}");

            if (donation.PaymentStatus != CommonConstants.TransactionStatus.Success)
            {
                _logger.LogWarning(
                    $"DonationActor: Donation status is not SUCCESS (it's {donation.PaymentStatus}). Will not process further.");

                // Double-check if we need to update the status
                var donationToUpdate =
                    await _db.Donations.FirstOrDefaultAsync(d =>
                        d.TransactionReference == message.TransactionReference);
                if (donationToUpdate != null &&
                    donationToUpdate.PaymentStatus != CommonConstants.TransactionStatus.Success)
                {
                    _logger.LogInformation(
                        $"DonationActor: Updating donation status to SUCCESS for {message.TransactionReference}");
                    donationToUpdate.PaymentStatus = CommonConstants.TransactionStatus.Success;
                    await _db.SaveChangesAsync();
                    _logger.LogInformation($"DonationActor: Successfully updated donation status");
                }

                return;
            }

            _logger.LogInformation(
                $"DonationActor: Processing donation {message.TransactionReference} with parallel tasks");

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
            _logger.LogInformation(
                $"DonationActor: No phone number provided for donation: {donation.TransactionReference}, skipping SMS notification");
            return;
        }

        try
        {
            _logger.LogInformation($"DonationActor: Creating service scope for SMS sending");
            using var scope = _serviceProvider.CreateScope();
            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

            // Ensure the phone number is properly formatted (remove any spaces or special characters)
            var cleanedPhoneNumber = donation.DonorPhone.Trim().Replace(" ", "");
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

            var messageText = $"Thank you for your donation of {donation.Amount} {donation.Currency} to Ramadan Relief. Your generosity will help provide {CalculateMeals(donation.Amount)} meals for those in need during the holy month.";
            _logger.LogInformation($"DonationActor: SMS message: {messageText}");

            var smsKey = "2nwkmCOVenT5pV0BZMFFiDnsn";
            var sender = "RamRelief25";
            
            // Properly encode the message for URL transmission
            var encodedMessage = Uri.EscapeDataString(messageText);
            var requestUrl = $"https://apps.mnotify.net/smsapi?key={smsKey}&to={cleanedPhoneNumber}&msg={encodedMessage}&sender_id={sender}";
            
            _logger.LogInformation($"DonationActor: Sending SMS request to mNotify API: {requestUrl}");
            var mnotifyResponse = await httpClient.GetAsync(requestUrl);

            if (mnotifyResponse.IsSuccessStatusCode)
            {
                var result = await mnotifyResponse.Content.ReadAsStringAsync();
                _logger.LogInformation(
                    $"DonationActor: SMS notification sent successfully for {donation.TransactionReference}. Result: {result}");
            }
            else
            {
                var errorContent = await mnotifyResponse.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    $"DonationActor: Failed to send SMS for {donation.TransactionReference}. Status: {mnotifyResponse.StatusCode}, Error: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                $"DonationActor: Error sending thank you SMS for donation: {donation.TransactionReference}");
            // We don't rethrow here as SMS failure shouldn't stop the entire process
        }
    }

    private async Task UpdateDonationStatistics(decimal amount)
    {
        _logger.LogInformation($"DonationActor: Updating donation statistics for amount: {amount}");

        try
        {
            // Use a simpler approach with retries
            const int maxRetries = 3;
            int retryCount = 0;
            bool success = false;

            while (!success && retryCount < maxRetries)
            {
                try
                {
                    retryCount++;
                    _logger.LogInformation($"DonationActor: Attempting to update statistics (Attempt {retryCount}/{maxRetries})");
                    
                    // Start a transaction
                    using var transaction = await _db.Database.BeginTransactionAsync();
                    
                    // Get the current statistics record - avoid using raw SQL
                    var stats = await _db.DonationStatistics.FirstOrDefaultAsync(s => s.Id == 1);
                    
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
                        _logger.LogInformation(
                            $"DonationActor: Updating existing statistics. Current values - Total: {stats.TotalDonations}, Donors: {stats.TotalDonors}, Meals: {stats.MealsServed}");
                        stats.TotalDonations += amount;
                        stats.TotalDonors += 1;
                        stats.MealsServed += CalculateMeals(amount);
                        stats.LastUpdated = DateTime.UtcNow;
                        _db.DonationStatistics.Update(stats);
                        _logger.LogInformation(
                            $"DonationActor: Updated statistics. New values - Total: {stats.TotalDonations}, Donors: {stats.TotalDonors}, Meals: {stats.MealsServed}");
                    }
                    
                    // Save changes
                    await _db.SaveChangesAsync();
                    
                    // Commit transaction
                    await transaction.CommitAsync();
                    
                    _logger.LogInformation("DonationActor: Statistics update completed successfully");
                    success = true;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    _logger.LogWarning(ex, $"DonationActor: Concurrency conflict during statistics update (Attempt {retryCount}/{maxRetries})");
                    // Let it retry for concurrency conflicts
                    await Task.Delay(200 * retryCount); // Exponential backoff
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"DonationActor: Error during statistics update (Attempt {retryCount}/{maxRetries})");
                    throw; // Rethrow other exceptions
                }
            }

            if (!success)
            {
                throw new Exception($"Failed to update donation statistics after {maxRetries} attempts");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DonationActor: Error updating donation statistics");
            throw; // Rethrow to allow the calling method to handle it
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