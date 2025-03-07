using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using RamadanReliefAPI.Extensions;

namespace RamadanReliefAPI.Services.Providers;

public class JwtTokenGenerator
{
    public static string GenerateUserJwtToken(
        IConfiguration configuration,
        string id,
        string username
    )
    {
        var tokenHandler = new JwtSecurityTokenHandler();

        var secretKey = configuration.GetValue<string>("ApiSettings:Secret");

        var key = Encoding.ASCII.GetBytes(secretKey);
        var tokenDescriptor = new SecurityTokenDescriptor()
        {
            Subject = new ClaimsIdentity(
                new Claim[]
                {
                    new Claim("Id", id),
                    new Claim("Username", username),
                    new Claim(ClaimTypes.Role, CommonConstants.Roles.Admin)
                }
            ),
            Expires = DateTime.UtcNow.AddDays(365),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature
            ),
            Issuer = "CONSTRUCTION PROJECT"
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var stringToken = tokenHandler.WriteToken(token);

        return stringToken;
    }
}