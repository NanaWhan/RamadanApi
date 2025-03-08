using Akka.Actor;
using Microsoft.EntityFrameworkCore;
using RamadanReliefAPI.Actors;
using RamadanReliefAPI.Actors.Messages;
using RamadanReliefAPI.Data;
using RamadanReliefAPI.Extensions;
using RamadanReliefAPI.Services.Interfaces;

namespace RamadanReliefAPI.Services.Providers;

public class PaymentVerificationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentVerificationService> _logger;
    private readonly TimeSpan _verificationInterval = TimeSpan.FromMinutes(1); // Run more frequently for testing

    public PaymentVerificationService(
        IServiceProvider serviceProvider,
        ILogger<PaymentVerificationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Payment Verification Service is starting.");

        // Initial delay to allow the application to fully start
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await VerifyPendingPayments(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while verifying pending payments.");
            }

            // Wait for the next check
            await Task.Delay(_verificationInterval, stoppingToken);
        }

        _logger.LogInformation("Payment Verification Service is stopping.");
    }

    private async Task VerifyPendingPayments(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Checking for pending payments to verify...");

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payStackService = scope.ServiceProvider.GetRequiredService<IPayStackPaymentService>();

        // Get all pending payments regardless of age for now
        // Remove time restriction for testing
        var pendingDonations = await dbContext.Donations
            .Where(d => d.PaymentStatus.ToUpper() == "PENDING")
            .ToListAsync(stoppingToken);

        // Log all found donations for debugging
        foreach (var donation in pendingDonations)
        {
            _logger.LogInformation($"Found pending donation: ID={donation.Id}, Reference={donation.TransactionReference}, Date={donation.DonationDate}, Status={donation.PaymentStatus}");
        }

        if (!pendingDonations.Any())
        {
            _logger.LogInformation("No pending donations found to verify.");
            
            // For debugging, check if there are any donations at all
            var totalCount = await dbContext.Donations.CountAsync(stoppingToken);
            var pendingCount = await dbContext.Donations.Where(d => d.PaymentStatus.ToUpper() == "PENDING").CountAsync(stoppingToken);
            var successCount = await dbContext.Donations.Where(d => d.PaymentStatus.ToUpper() == "SUCCESS").CountAsync(stoppingToken);
            var failedCount = await dbContext.Donations.Where(d => d.PaymentStatus.ToUpper() == "FAILED").CountAsync(stoppingToken);
            var otherCount = totalCount - pendingCount - successCount - failedCount;
            
            _logger.LogInformation($"Database stats: Total={totalCount}, Pending={pendingCount}, Success={successCount}, Failed={failedCount}, Other={otherCount}");
            
            return;
        }

        _logger.LogInformation($"Found {pendingDonations.Count} pending donations to verify.");

        foreach (var donation in pendingDonations)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                _logger.LogInformation($"Verifying payment for donation: {donation.TransactionReference}");
                
                var verification = payStackService.VerifyTransaction(donation.TransactionReference);
                
                if (verification != null && verification.Status)
                {
                    _logger.LogInformation($"PayStack verification result for {donation.TransactionReference}: {verification.Data.Status}");
                    
                    if (verification.Data.Status.ToLower() == "success")
                    {
                        // Update donation status to success
                        donation.PaymentStatus = CommonConstants.TransactionStatus.Success;
                        await dbContext.SaveChangesAsync(stoppingToken);
                        
                        _logger.LogInformation($"Updated donation {donation.TransactionReference} status to SUCCESS");
                        
                        // Notify the actor system
                        TopLevelActor.DonationActor.Tell(new DonationCompletedMessage(
                            donation.TransactionReference,
                            donation.Amount,
                            donation.DonorPhone
                        ));
                        
                        _logger.LogInformation($"Notified DonationActor about completed donation: {donation.TransactionReference}");
                    }
                    else if (verification.Data.Status.ToLower() == "failed")
                    {
                        donation.PaymentStatus = CommonConstants.TransactionStatus.Failed;
                        await dbContext.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation($"Updated donation {donation.TransactionReference} status to FAILED");
                    }
                    else
                    {
                        _logger.LogInformation($"Donation {donation.TransactionReference} has status '{verification.Data.Status}' in PayStack, not updating");
                    }
                }
                else
                {
                    _logger.LogWarning($"Failed to verify payment for donation: {donation.TransactionReference}. Response: {(verification == null ? "null" : $"Status={verification.Status}, Message={verification.Message}")}");
                }
                
                // Brief pause between verification requests to avoid rate limiting
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error verifying payment for donation: {donation.TransactionReference}");
                // Continue with the next donation
            }
        }
    }
}