using System.ComponentModel.DataAnnotations;
using System.Net;
using Akka.Actor;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RamadanReliefAPI.Actors;
using RamadanReliefAPI.Actors.Messages;
using RamadanReliefAPI.Data;
using RamadanReliefAPI.Models;
using RamadanReliefAPI.Models.DomainModels;
using RamadanReliefAPI.Models.Dtos.Donation.Events;

namespace RamadanReliefAPI.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<EventsController> _logger;
    private ApiResponse _apiResponse;

    /// <summary>
    ///  Constructor
    /// </summary>
    /// <param name="db"></param>
    /// <param name="logger"></param>
    public EventsController(
        ApplicationDbContext db,
        ILogger<EventsController> logger)
    {
        _db = db;
        _logger = logger;
        _apiResponse = new ApiResponse();
    }

    /// <summary>
    /// Get all active events
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllEvents()
    {
        try
        {
            _logger.LogInformation("Fetching all active events");
            var events = await _db.Events
                .Where(e => e.IsActive)
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    e.Description,
                    e.Location,
                    e.EventDate,
                    e.RegistrationDeadline,
                    e.MaxAttendees,
                    e.CurrentAttendees,
                    AvailableSlots = e.MaxAttendees - e.CurrentAttendees,
                    IsRegistrationOpen = e.RegistrationDeadline > DateTime.UtcNow && e.CurrentAttendees < e.MaxAttendees
                })
                .ToListAsync();

            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Result = events;

            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching events");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong while fetching events";
            _apiResponse.Errors = new List<string> { ex.Message };

            return StatusCode(500, _apiResponse);
        }
    }

    /// <summary>
    /// Get event details by ID
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetEventById(Guid id)
    {
        try
        {
            _logger.LogInformation($"Fetching event with ID: {id}");
            var eventItem = await _db.Events.FindAsync(id);

            if (eventItem == null || !eventItem.IsActive)
            {
                _logger.LogWarning($"Event with ID {id} not found or not active");
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.NotFound;
                _apiResponse.Message = "Event not found";
                return NotFound(_apiResponse);
            }

            var eventDetails = new
            {
                eventItem.Id,
                eventItem.Title,
                eventItem.Description,
                eventItem.Location,
                eventItem.EventDate,
                eventItem.RegistrationDeadline,
                eventItem.MaxAttendees,
                eventItem.CurrentAttendees,
                AvailableSlots = eventItem.MaxAttendees - eventItem.CurrentAttendees,
                IsRegistrationOpen = eventItem.RegistrationDeadline > DateTime.UtcNow && eventItem.CurrentAttendees < eventItem.MaxAttendees
            };

            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.OK;
            _apiResponse.Result = eventDetails;

            return Ok(_apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching event with ID: {id}");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong while fetching the event";
            _apiResponse.Errors = new List<string> { ex.Message };

            return StatusCode(500, _apiResponse);
        }
    }

    /// <summary>
    /// Register for an event
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RegisterForEvent([FromBody] EventRegistrationRequest request)
    {
        try
        {
            _logger.LogInformation($"Processing registration for event ID: {request.EventId}");
            
            // Find the event
            var eventItem = await _db.Events.FindAsync(request.EventId);
            
            if (eventItem == null || !eventItem.IsActive)
            {
                _logger.LogWarning($"Event with ID {request.EventId} not found or not active");
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.NotFound;
                _apiResponse.Message = "Event not found";
                return NotFound(_apiResponse);
            }
            
            // Check if registration is still open
            if (eventItem.RegistrationDeadline < DateTime.UtcNow)
            {
                _logger.LogWarning($"Registration deadline has passed for event ID: {request.EventId}");
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.BadRequest;
                _apiResponse.Message = "Registration deadline has passed";
                return BadRequest(_apiResponse);
            }
            
            // Check if there are enough slots available
            if (eventItem.CurrentAttendees + request.NumberOfPeople > eventItem.MaxAttendees)
            {
                _logger.LogWarning($"Not enough slots available for event ID: {request.EventId}");
                _apiResponse.IsSuccess = false;
                _apiResponse.StatusCode = HttpStatusCode.BadRequest;
                _apiResponse.Message = $"Only {eventItem.MaxAttendees - eventItem.CurrentAttendees} slots available";
                return BadRequest(_apiResponse);
            }
            
            // Using a transaction to ensure data consistency
            using var transaction = await _db.Database.BeginTransactionAsync();
            
            try
            {
                // Create registration record
                var registration = new EventRegistration
                {
                    EventId = request.EventId,
                    AttendeeName = request.FullName,
                    AttendeeEmail = request.Email,
                    AttendeePhone = request.PhoneNumber,
                    NumberOfPeople = request.NumberOfPeople,
                    RegistrationDate = DateTime.UtcNow
                };
                
                await _db.EventRegistrations.AddAsync(registration);
                
                // Update event attendee count
                eventItem.CurrentAttendees += request.NumberOfPeople;
                _db.Events.Update(eventItem);
                
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                
                _logger.LogInformation($"Registration successful for event ID: {request.EventId}");
                
                // Send SMS notification if we have an actor for that
                try
                {
                    if (TopLevelActor.MainActor != ActorRefs.Nobody)
                    {
                        _logger.LogInformation($"Sending event registration confirmation to {request.PhoneNumber}");
                        TopLevelActor.MainActor.Tell(new EventRegistrationMessage(
                            request.EventId,
                            request.FullName,
                            request.PhoneNumber,
                            request.NumberOfPeople));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error sending event registration SMS: {ex.Message}");
                    // Don't return an error - the registration was still successful
                }
                
                _apiResponse.IsSuccess = true;
                _apiResponse.StatusCode = HttpStatusCode.OK;
                _apiResponse.Message = "Registration successful";
                _apiResponse.Result = new
                {
                    RegistrationId = registration.Id,
                    EventTitle = eventItem.Title,
                    EventLocation = eventItem.Location,
                    EventDate = eventItem.EventDate,
                    NumberOfPeople = request.NumberOfPeople
                };
                
                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error during registration transaction");
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing event registration");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong while processing your registration";
            _apiResponse.Errors = new List<string> { ex.Message };
            
            return StatusCode(500, _apiResponse);
        }
    }

    /// <summary>
    /// Create a new event (admin only)
    /// </summary>
    [HttpPost("create")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateEvent([FromBody] EventCreateRequest request)
    {
        try
        {
            _logger.LogInformation($"Creating new event: {request.Title}");
            
            var newEvent = new Event
            {
                Title = request.Title,
                Description = request.Description,
                Location = request.Location,
                EventDate = request.EventDate,
                RegistrationDeadline = request.RegistrationDeadline,
                MaxAttendees = request.MaxAttendees,
                CurrentAttendees = 0,
                IsActive = true
            };
            
            await _db.Events.AddAsync(newEvent);
            await _db.SaveChangesAsync();
            
            _logger.LogInformation($"Event created with ID: {newEvent.Id}");
            
            _apiResponse.IsSuccess = true;
            _apiResponse.StatusCode = HttpStatusCode.Created;
            _apiResponse.Message = "Event created successfully";
            _apiResponse.Result = new
            {
                EventId = newEvent.Id,
                newEvent.Title,
                newEvent.Location,
                newEvent.EventDate
            };
            
            return StatusCode(201, _apiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event");
            _apiResponse.StatusCode = HttpStatusCode.InternalServerError;
            _apiResponse.Message = "Something went wrong while creating the event";
            _apiResponse.Errors = new List<string> { ex.Message };
            
            return StatusCode(500, _apiResponse);
        }
    }
}


/// <summary>
/// DtO for event registration request
/// </summary>
public class EventCreateRequest
{
    [Required(ErrorMessage = "Title is required")]
    public string Title { get; set; }
    
    [Required(ErrorMessage = "Description is required")]
    public string Description { get; set; }
    
    [Required(ErrorMessage = "Location is required")]
    public string Location { get; set; }
    
    [Required(ErrorMessage = "Event date is required")]
    public DateTime EventDate { get; set; }
    
    [Required(ErrorMessage = "Registration deadline is required")]
    public DateTime RegistrationDeadline { get; set; }
    
    [Required(ErrorMessage = "Max attendees is required")]
    [Range(1, 10000, ErrorMessage = "Max attendees must be between 1 and 10000")]
    public int MaxAttendees { get; set; }
}