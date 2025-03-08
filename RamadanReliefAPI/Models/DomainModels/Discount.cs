namespace RamadanReliefAPI.Models.DomainModels;

public class Discount : BaseDataModel
{
    public string Code { get; set; }
    public decimal PercentageOff { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiryDate { get; set; }
    public int? MaxUses { get; set; }
    public int UsedCount { get; set; } = 0;
}