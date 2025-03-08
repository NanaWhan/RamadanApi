using System.Net;
using Akka.Actor;
using Microsoft.AspNetCore.Mvc;
using RamadanReliefAPI.Actors;
using RamadanReliefAPI.Actors.Messages;
using RamadanReliefAPI.Data;
using RamadanReliefAPI.Models;
using RamadanReliefAPI.Models.DomainModels;

namespace RamadanReliefAPI.Controllers;

[ApiController]
[Route("api/newsletter")]
public class NewsLetterController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private ApiResponse _apiResponse;

    public NewsLetterController(ApplicationDbContext db)
    {
        _db = db;
        _apiResponse = new ApiResponse();
    }

    /// <summary>
    /// create sms newsletter
    /// </summary>
    /// <param name="sendNewsLettersRequest"></param>
    /// <returns></returns>
    [HttpPost("create")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateNewsLetter(SendNewsLettersRequest sendNewsLettersRequest)
    {
        try
        {
            var newsLetter = new NewsLetter() { PhoneNumber = sendNewsLettersRequest.PhoneNumber };

            await _db.NewsLetters.AddAsync(newsLetter);
            await _db.SaveChangesAsync();

            TopLevelActor.MainActor.Tell(new NewsLetterMessage(sendNewsLettersRequest.PhoneNumber));

            _apiResponse.IsSuccess = true;
            _apiResponse.Message = "Newsletter saved";
            _apiResponse.StatusCode = HttpStatusCode.OK;
            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Oops..Something went wrong";
            _apiResponse.Errors = new List<string>() { ex.Message.ToString() };

            return BadRequest(_apiResponse);
        }
    }
}

public class SendNewsLettersRequest
{
    public string PhoneNumber { get; set; }
}