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
    // Add a HashSet to track donations that have already been processed
    private readonly HashSet<string> _processedDonations = new HashSet<string>();

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

        // Check if this donation has already been processed
        if (_processedDonations.Contains(message.TransactionReference))
        {
            _logger.LogInformation(
                $"DonationActor: Donation {message.TransactionReference} has already been processed. Skipping to avoid duplicate messages.");
            return;
        }

        try
        {
            // Find the donation
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

            // Double-check status and update if needed
            if (donation.PaymentStatus != CommonConstants.TransactionStatus.Success)
            {
                _logger.LogWarning(
                    $"DonationActor: Donation status is not SUCCESS (it's {donation.PaymentStatus}). Checking if need to update.");

                var donationToUpdate = await _db.Donations.FirstOrDefaultAsync(d =>
                    d.TransactionReference == message.TransactionReference);

                if (donationToUpdate != null &&
                    donationToUpdate.PaymentStatus != CommonConstants.TransactionStatus.Success)
                {
                    _logger.LogInformation(
                        $"DonationActor: Updating donation status to SUCCESS for {message.TransactionReference}");
                    donationToUpdate.PaymentStatus = CommonConstants.TransactionStatus.Success;
                    await _db.SaveChangesAsync();
                    _logger.LogInformation($"DonationActor: Successfully updated donation status");

                    // Now that we've updated the status to SUCCESS, we should proceed with processing
                    donation = donationToUpdate;
                }
                else
                {
                    return; // No update needed and not success status, so don't continue
                }
            }

            _logger.LogInformation(
                $"DonationActor: Processing donation {message.TransactionReference} with parallel tasks");

            // Execute these tasks
            var sendSmsTask = SendThankYouSms(donation);
            var updateStatsTask = UpdateDonationStatistics(donation.Amount);

            // Wait for both tasks to complete
            await Task.WhenAll(sendSmsTask, updateStatsTask);

            // Add to processed donations set after successful processing
            _processedDonations.Add(message.TransactionReference);

            _logger.LogInformation($"DonationActor: Successfully processed donation: {message.TransactionReference}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"DonationActor: Error processing completed donation: {message.TransactionReference}");
            // Consider implementing a retry mechanism for critical failures
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

            // Ensure the phone number is properly formatted
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

            // Calculate meals
            int meals = CalculateMeals(donation.Amount);

            var messageText =
                $"Thank you {donation.DonorName}, for your donation of {donation.Amount} {donation.Currency} to Ramadan Relief. Your generosity will help provide meals for those in need during the holy month.";
            _logger.LogInformation($"DonationActor: SMS message: {messageText}");

            var smsKey = "2nwkmCOVenT5pV0BZMFFiDnsn";
            var sender = "RamRelief25";

            // Properly encode the message for URL transmission
            var encodedMessage = Uri.EscapeDataString(messageText);
            var requestUrl =
                $"https://apps.mnotify.net/smsapi?key={smsKey}&to={cleanedPhoneNumber}&msg={encodedMessage}&sender_id={sender}";

            _logger.LogInformation($"DonationActor: Sending SMS request to mNotify API: {requestUrl}");
            var mnotifyResponse = await httpClient.GetAsync(requestUrl);

            var responseContent = await mnotifyResponse.Content.ReadAsStringAsync();
            _logger.LogInformation(
                $"DonationActor: mNotify API response: {mnotifyResponse.StatusCode} - {responseContent}");

            if (mnotifyResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    $"DonationActor: SMS notification sent successfully for {donation.TransactionReference}. Result: {responseContent}");
            }
            else
            {
                _logger.LogWarning(
                    $"DonationActor: Failed to send SMS for {donation.TransactionReference}. Status: {mnotifyResponse.StatusCode}, Error: {responseContent}");
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
            // Use a straightforward approach with explicit locking via transaction
            using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                // Get the current statistics record with tracking enabled
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
                    _db.DonationStatistics.Add(stats);
                }
                else
                {
                    _logger.LogInformation(
                        $"DonationActor: Updating existing statistics. Current values - Total: {stats.TotalDonations}, Donors: {stats.TotalDonors}, Meals: {stats.MealsServed}");

                    // Only update the relevant fields
                    stats.TotalDonations += amount;
                    // Don't increment donors count here, as this could be a repeat donation
                    // We'll handle donor counting differently
                    stats.MealsServed += CalculateMeals(amount);
                    stats.LastUpdated = DateTime.UtcNow;

                    _db.DonationStatistics.Update(stats);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    $"DonationActor: Successfully updated statistics. New values - Total: {stats.TotalDonations}, Donors: {stats.TotalDonors}, Meals: {stats.MealsServed}");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "DonationActor: Error during statistics update transaction");
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DonationActor: Error updating donation statistics");
            throw;
        }
    }

    private int CalculateMeals(decimal amount)
    {
        // Assuming 1 meal costs 10 GHS
        _logger.LogInformation($"DonationActor: Calculating meals for amount {amount}. (Rate: 1 meal = 10 GHS)");
        int meals = (int)(amount / 10);
        _logger.LogInformation($"DonationActor: Calculated {meals} meals for amount {amount}");
        return meals;
    }
}