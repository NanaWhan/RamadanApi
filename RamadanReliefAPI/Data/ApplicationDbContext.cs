using Microsoft.EntityFrameworkCore;
using RamadanReliefAPI.Models.DomainModels;

namespace RamadanReliefAPI.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        AppContext.SetSwitch("Npgsql.DisableDateTimeInfinityConversions", true);
    }

    public DbSet<Admin> Admins { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Volunteer> Volunteers { get; set; }
    public DbSet<Donation> Donations { get; set; }
    public DbSet<Event> Events { get; set; }
    public DbSet<EventRegistration> EventRegistrations { get; set; }
    public DbSet<DonationStatistics> DonationStatistics { get; set; }
    public DbSet<NewsLetter> NewsLetters { get; set; }
    public DbSet<Discount> Discounts { get; set; }
    public DbSet<Partners> Partners { get; set; }
}