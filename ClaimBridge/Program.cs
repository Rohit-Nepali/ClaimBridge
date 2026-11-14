using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using ClaimBridge.Common;
using ClaimBridge.Data;
using ClaimBridge.Infrastructure;
using ClaimBridge.Models;
using ClaimBridge.Options;
using ClaimBridge.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

// ---------------------------------------------------------------------------
// DATA FLOW (startup / bootstrap)
//   1. WebApplication.CreateBuilder(args) builds the host configuration.
//      Data source: command-line args -> appsettings.json -> appsettings.{Env}.json
//      -> user secrets (Development) -> environment variables -> DI container,
//      all merged into IConfiguration.
//   2. Services registered below are placed into the DI container. They are
//      resolved later, per request, when the request pipeline asks for them.
//   3. app.Build() freezes the service container and produces the runnable app.
//   4. The middleware pipeline (Use*/Map*) is composed top-to-bottom. Each
//      incoming HTTP request flows DOWN this chain, and each response flows
//      back UP in reverse order.
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// DATA FLOW: Registers the MVC controller subsystem. When a request reaches a
// controller, the framework uses this registration to model-bind the request
// body/query into action parameters and to serialize the returned object back
// into the HTTP response body.
builder.Services.AddControllers();

// DATA FLOW: Registers the OpenAPI document generator. It reads controller
// metadata (routes, models, attributes) at startup and exposes a generated
// JSON schema document that Scalar later consumes for the UI. The document
// transformer injects the JWT bearer security scheme so Scalar can render an
// "Authorize" button.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter a JWT access token."
        };

        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
        });

        return Task.CompletedTask;
    });
});

// DATA FLOW: Binds the "Jwt" configuration section to JwtSettings and validates
// it at startup (issuer/audience/key present, key at least 32 chars). The real
// key comes from user secrets, never from source control.
builder.Services
    .AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection(JwtSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var jwtSettings = builder.Configuration
    .GetSection(JwtSettings.SectionName)
    .Get<JwtSettings>() ?? throw new InvalidOperationException("JWT configuration is missing.");

// DATA FLOW: Registers the EF Core SQL Server context backed by the existing
// "DefaultConnection" connection string (local SQL Express, Windows auth).
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.")));

// DATA FLOW: Registers ASP.NET Core Identity (users, roles, password hashing,
// lockout tracking) and points its stores at AppDbContext. The password and
// lockout policies below are enforced by UserManager/SignInManager.
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);

        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// DATA FLOW: Configures JWT bearer authentication as the default scheme. Each
// request's Authorization header is validated against the configured issuer,
// audience, signing key and lifetime with zero clock skew. Role claims are
// mapped from ClaimTypes.Role so [Authorize(Roles = "Admin")] works.
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = ClaimNames.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization();

// DATA FLOW: Named CORS policy whose allowed origins come from configuration.
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    options.AddPolicy("CorsPolicy", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

// DATA FLOW: Per-IP fixed-window rate limiting. The "auth" policy is applied to
// the login/register actions via [EnableRateLimiting("auth")].
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests.",
            Detail = "Please try again later."
        }, cancellationToken);
    };
});

// DATA FLOW: Routes unhandled exceptions to the custom IExceptionHandler, which
// logs the failure and writes a ProblemDetails response without leaking
// internal details or secrets.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// DATA FLOW: Materializes the DI container and configuration into a live host.
var app = builder.Build();

// Configure the HTTP request pipeline.
// DATA FLOW: Development only. The incoming request for the OpenAPI JSON is
// served here; the Scalar UI reads that JSON and renders the interactive docs.
if (app.Environment.IsDevelopment())
{
    // Exposes the generated OpenAPI document (typically at /openapi/v1.json).
    app.MapOpenApi();
    // DATA FLOW: Serves the Scalar UI (typically /scalar), which fetches the
    // OpenAPI document above and displays it as a browsable API reference.
    app.MapScalarApiReference(options => options.AddPreferredSecuritySchemes(["Bearer"]));
}

// DATA FLOW: Catches any exception bubbling out of the pipeline and delegates
// it to the registered IExceptionHandler before the response is written.
app.UseExceptionHandler();

// DATA FLOW: Inspects each request's scheme/port; if it arrived over plain HTTP,
// it rewrites the response to a redirect so the client retries over HTTPS.
app.UseHttpsRedirection();

// DATA FLOW: Selects the endpoint (controller action) from the request URL so
// subsequent middleware can read endpoint metadata such as rate-limit policies.
app.UseRouting();

// DATA FLOW: Applies the named CORS policy to cross-origin requests.
app.UseCors("CorsPolicy");

// DATA FLOW: Enforces the rate-limit policy attached to the selected endpoint
// (login/register) before the request reaches authentication or the action.
app.UseRateLimiter();

// DATA FLOW: Reads and validates the JWT on the Authorization header and builds
// the ClaimsPrincipal that authorization and controllers consume.
app.UseAuthentication();

// DATA FLOW: Evaluates the caller's authorization policies/claims (populated by
// the authentication layer above) and blocks requests that fail the policy
// before they reach a controller.
app.UseAuthorization();

// DATA FLOW: Matches the incoming request URL + verb to a controller action,
// lets model binding read the request data into parameters, invokes the action,
// then writes the action's return value as the response back up the pipeline.
app.MapControllers();

// DATA FLOW: Idempotently ensures the Admin and User roles exist before the
// application starts accepting requests.
await RoleSeeder.SeedRolesAsync(app.Services);

// DATA FLOW: Starts the Kestrel listener. From here, requests are accepted and
// pushed through the pipeline above until the host is shut down.
app.Run();
