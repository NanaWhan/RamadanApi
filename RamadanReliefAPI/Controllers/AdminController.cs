using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RamadanReliefAPI.Data;
using RamadanReliefAPI.Models;
using RamadanReliefAPI.Models.DomainModels;
using RamadanReliefAPI.Models.Dtos.Admin;
using RamadanReliefAPI.Services.Providers;
using BC = BCrypt.Net.BCrypt;


namespace RamadanReliefAPI.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    private readonly IConfiguration _configuration;

    private ApiResponse _apiResponse;

    public AdminController(ApplicationDbContext db, IConfiguration configuration)
    {
        _db = db;
        _apiResponse = new ApiResponse();
        _configuration = configuration;
    }

    /// <summary>
    /// Create admin
    /// </summary>
    /// <param name="createAdminRequest"></param>
    /// <returns></returns>
    [HttpPost("create")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateAdminAccount(CreateAdminRequest createAdminRequest)
    {
        try
        {
            var admin = new Admin()
            {
                Username = createAdminRequest.Username,
                Password = BC.HashPassword(createAdminRequest.Password),
            };

            await _db.Admins.AddAsync(admin);
            await _db.SaveChangesAsync();

            _apiResponse.IsSuccess = true;
            _apiResponse.Message = "Admin account created";
            _apiResponse.StatusCode = HttpStatusCode.Created;
            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong";
            _apiResponse.Errors = new List<string>() { ex.Message.ToString() };

            return BadRequest(_apiResponse);
        }
    }

    /// <summary>
    /// Login admin
    /// </summary>
    /// <param name="createAdminRequest"></param>
    /// <returns></returns>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> LoginInAdmin(LoginAdminRequest loginAdminRequest)
    {
        try
        {
            var admin = await _db.Admins
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Username == loginAdminRequest.Username);

            if (admin is null)
            {
                _apiResponse.IsSuccess = true;
                _apiResponse.Message = "Invalid credentials";
                _apiResponse.StatusCode = HttpStatusCode.NotFound;
                return NotFound(_apiResponse);
            }

            var isPasswordValid = BC.Verify(loginAdminRequest.Password, admin.Password);

            if (!isPasswordValid)
            {
                _apiResponse.IsSuccess = true;
                _apiResponse.Message = "Invalid credentials";
                _apiResponse.StatusCode = HttpStatusCode.NotFound;
                return NotFound(_apiResponse);
            }
            var token = JwtTokenGenerator.GenerateUserJwtToken(
                _configuration,
                admin.Id,
                admin.Username
            );

            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Result = token;
            _apiResponse.Message = "Login successful";
            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong";
            _apiResponse.Errors = new List<string>() { ex.Message.ToString() };

            return BadRequest(_apiResponse);
        }
    }
}