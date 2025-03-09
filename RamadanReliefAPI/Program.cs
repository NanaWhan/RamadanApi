using System.Reflection;
using System.Text;
using Akka.Actor;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RamadanReliefAPI.Data;
using RamadanReliefAPI.Extensions;
using RamadanReliefAPI.Services.Interfaces;
using RamadanReliefAPI.Services.Providers;
using Supabase;

var builder = WebApplication.CreateBuilder(args);
var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.XML";
var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
string corsPolicyName = "RamadanProject.PolicyName";

// Configure logging first
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.
builder.Services.AddControllers();

// Configure Supabase client
var url = "https://samobihwxdfcbxpmqkqc.supabase.co";
var supabsekey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InNhbW9iaWh3eGRmY2J4cG1xa3FjIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NDA5NzUxNTYsImV4cCI6MjA1NjU1MTE1Nn0.icy6G2XjKTZHUQDM5B_p5GSsw3LWlqYzpldL4hhJwJg";
var options = new SupabaseOptions()
{
    AutoRefreshToken = true,
    AutoConnectRealtime = true,
};

// Register Supabase client
builder.Services.AddSingleton(provider => new Supabase.Client(url, supabsekey, options));

// Configure database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnectionString")));

// Configure HttpClient
builder.Services.AddHttpClient();

// Register application services
builder.Services.AddScoped<IPayStackPaymentService, PayStackPaymentService>();

// Configure Akka.NET actor system
builder.Services.AddActorSystem("ramadan-actor-system");

// Add payment verification background service
builder.Services.AddHostedService<PaymentVerificationService>();
builder.Services.AddHostedService<ActorSystemHealthService>();
builder.Services.AddHostedService<ActorSystemHealthService>();

// Configure JWT Authentication
var key = builder.Configuration.GetValue<string>("ApiSettings:Secret");
builder.Services.AddAuthentication(x =>
{
    x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(x =>
{
    x.RequireHttpsMetadata = false;
    x.SaveToken = true;
    x.TokenValidationParameters = new TokenValidationParameters()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key)),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true
    };
});

// Configure CORS
builder.Services.AddCors(options =>
    options.AddPolicy(corsPolicyName, policy => 
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// Configure Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme()
    {
        Description = "JWT Authorization header using the Bearer scheme. \r\n\r\n "
            + "Enter 'Bearer' [space] and then your token in the text input below.\r\n\r\n"
            + "Example: \"Bearer 12345abcdef\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Scheme = "Bearer"
    });
    
    options.AddSecurityRequirement(new OpenApiSecurityRequirement()
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = ParameterLocation.Header
            },
            new List<string>()
        }
    });

    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1.0",
        Title = "Ramadan Relief API",
        Description = "MAIN WEB API / Admin API",
    });

    options.IncludeXmlComments(xmlPath);
});

// Build the application
var app = builder.Build();


// Configure middleware pipeline
app.UseCors(corsPolicyName);

// Configure Swagger UI
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
});

// Configure authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// Map controllers
app.MapControllers();

// Start the application
app.Run();

// Actor system health check service
public class ActorSystemHealthService : IHostedService
{
    private readonly ILogger<ActorSystemHealthService> _logger;

    public ActorSystemHealthService(ILogger<ActorSystemHealthService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking actor system health on startup...");
        
        var actorStatus = new Dictionary<string, bool>
        {
            ["MainActor"] = RamadanReliefAPI.Actors.TopLevelActor.MainActor != ActorRefs.Nobody,
            ["DonationActor"] = RamadanReliefAPI.Actors.TopLevelActor.DonationActor != ActorRefs.Nobody,
            ["VolunteerActor"] = RamadanReliefAPI.Actors.TopLevelActor.VolunteerActor != ActorRefs.Nobody,
            ["PartnerActor"] = RamadanReliefAPI.Actors.TopLevelActor.PartnerActor != ActorRefs.Nobody
        };
        
        foreach (var actor in actorStatus)
        {
            _logger.LogInformation($"{actor.Key} initialized: {actor.Value}");
        }
        
        if (actorStatus.Values.All(v => v))
        {
            _logger.LogInformation("All actors successfully initialized!");
        }
        else
        {
            _logger.LogWarning("Some actors failed to initialize!");
        }
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}