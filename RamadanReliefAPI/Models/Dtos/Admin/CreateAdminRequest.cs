using System.ComponentModel.DataAnnotations;

namespace RamadanReliefAPI.Models.Dtos.Admin;

public class CreateAdminRequest
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "Admin username is required")]
    public string Username { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Admin password is required")]
    public string Password { get; set; }
}