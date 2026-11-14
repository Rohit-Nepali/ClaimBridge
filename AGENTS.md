# ClaimBridge

Single-project ASP.NET Core Web API on `net10.0` (SDK 10.0.401). Uses MVC controllers, not minimal APIs. Not a git repo. No tests, lint, or CI exist.

## Commands (run from repo root)

- Build: `dotnet build ClaimBridge.slnx`
- Run: `dotnet run --project ClaimBridge` → http://localhost:5078 (`http` launch profile is first; Visual Studio uses `https` → https://localhost:7126)
- API docs (Development only): Scalar UI at `/scalar`, OpenAPI JSON at `/openapi/v1.json`
- Add migration: `dotnet ef migrations add <Name> --project ClaimBridge --startup-project ClaimBridge --output-dir Data/Migrations`
- Apply migrations: `dotnet ef database update --project ClaimBridge --startup-project ClaimBridge`
- Requires the global tool: `dotnet tool install --global dotnet-ef --version 10.0.15`
- No test project exists. Create one before treating `dotnet test` as meaningful.

## Setup

- `Jwt:Key` must come from user secrets (UserSecretsId is configured). `appsettings.json` only has a placeholder:
  `dotnet user-secrets set "Jwt:Key" "<at-least-32-char-key>" --project ClaimBridge`
- The `AddAuth` migration has been generated but not applied. Role seeding runs at startup, so the database must be migrated before the app will start cleanly.
- Do not change `Properties/launchSettings.json` or the existing `DefaultConnection` string.

## Layout

- `ClaimBridge/Program.cs` — composition root: controllers, OpenAPI + Bearer scheme, EF Core, Identity, JWT bearer, CORS, rate limiting, exception handling, role seeding. Keep its `// DATA FLOW:` comments in the same style.
- `ClaimBridge/Data/` — `AppDbContext` (`IdentityDbContext<ApplicationUser>`), `RoleSeeder`, `AppDbContextFactory` (design-time only, reads appsettings), and `Migrations/`.
- `ClaimBridge/Models/` — `ApplicationUser` and `RefreshToken`.
- `ClaimBridge/DTOs/`, `Services/` (`IAuthService`/`AuthService`, `ITokenService`/`TokenService`), `Options/JwtSettings.cs`, `Infrastructure/GlobalExceptionHandler.cs`.
- `ClaimBridge/Controllers/` — `AuthController` at `/api/auth` (register/login/refresh/logout/me/change-password) and `SampleController` (protected + Admin-only).
- `appsettings.json` — `DefaultConnection` (local SQL Express `claim_bridge`), JWT placeholders, `Cors:AllowedOrigins`.

## Gotchas

- Solution uses the new `.slnx` format, not `.sln`.
- Identity: password min 8 with upper/lower/digit; lockout after 5 failures for 5 minutes.
- Refresh tokens are stored SHA-256 hashed; reusing a revoked token revokes all of that user's active tokens.
- JWT validation uses zero clock skew and `MapInboundClaims = false`; user id is the `sub` claim (see `Common/ClaimNames.cs`).
- `ClaimBridge/ClaimBridge.http` still calls a non-existent `/weatherforecast`; it is stale scaffold.
