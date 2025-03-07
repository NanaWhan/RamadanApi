namespace RamadanReliefAPI.Models.DomainModels;

public class DonationStatistics
{
    public int Id { get; set; } = 1; // Single record
    public decimal TotalDonations { get; set; }
    public int TotalDonors { get; set; }
    public int MealsServed { get; set; }
    public DateTime LastUpdated { get; set; }
}