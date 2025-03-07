using System.Reflection;
using System.Text;
using Akka.Actor;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RamadanReliefAPI.Data;
using Supabase;

var builder = WebApplication.CreateBuilder(args);
var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.XML";
var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
string corsPolicyName = "RamadanProject.PolicyName";

// Add services to the container.

var url = "https://samobihwxdfcbxpmqkqc.supabase.co";
var supabsekey =
    "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InNhbW9iaWh3eGRmY2J4cG1xa3FjIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NDA5NzUxNTYsImV4cCI6MjA1NjU1MTE1Nn0.icy6G2XjKTZHUQDM5B_p5GSsw3LWlqYzpldL4hhJwJg";
var options = new SupabaseOptions()
{
    AutoRefreshToken = true,
    AutoConnectRealtime = true,
    // SessionHandler = new SupabaseSessionHandler() <-- This must be implemented by the developer
};

// Note the creation as a singleton.

builder.Services.AddSingleton(provider => new Supabase.Client(url, supabsekey, options));

builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder
    .Services
    .AddSwaggerGen(options =>
    {
        options.AddSecurityDefinition(
            "Bearer",
            new OpenApiSecurityScheme()
            {
                Description =
                    "JWT Authorization header using the Bearer scheme. \r\n\r\n "
                    + "Enter 'Bearer' [space] and then your token in the text input below.\r\n\r\n"
                    + "Example: \"Bearer 12345abcdef\"",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Scheme = "Bearer"
            }
        );
        options.AddSecurityRequirement(
            new OpenApiSecurityRequirement()
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
            }
        );

        options.SwaggerDoc(
            "v1",
            new OpenApiInfo
            {
                Version = "v1.0",
                Title = "Ramadan Relief API",
                Description = "MAIN WEB API / Admin API",
            }
        );

        options.IncludeXmlComments(xmlPath);
    });
var key = builder.Configuration.GetValue<string>("ApiSettings:Secret");
builder
    .Services
    .AddAuthentication(x =>
    {
        x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
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

builder
    .Services
    .AddCors(
        options =>
            options.AddPolicy(
                corsPolicyName,
                policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
            )
    );

builder
    .Services
    .AddDbContext<ApplicationDbContext>(
        options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnectionString"))
    );
builder.Services.AddHttpClient();
var actorSystem = ActorSystem.Create("ramadan-actor-system");
builder.Services.AddSingleton(actorSystem);

var app = builder.Build();

app.UseCors(builder =>
{
    builder.AllowAnyOrigin();
    builder.AllowAnyMethod();
    builder.AllowAnyHeader();
});

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
    // options.RoutePrefix = string.Empty;
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();