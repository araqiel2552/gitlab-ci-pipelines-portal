using System.Security.Claims;
using GitLabPortal.Components;
using GitLabPortal.Data;
using GitLabPortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? "Data Source=gitlabportal.db";
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddDataProtection()
    .SetApplicationName("GitLabPortal")
    .PersistKeysToDbContext<AppDbContext>();

builder.Services.AddHttpClient(GitLabApiService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GitLabPortal/1.0");
});

builder.Services.AddSingleton<ITokenProtector, TokenProtector>();
builder.Services.AddScoped<GitLabApiService>();
builder.Services.AddScoped<PortalAccessService>();
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<IClaimsTransformation, PortalClaimsTransformation>();

var okta = builder.Configuration.GetSection("Okta");
var oktaConfigured = !string.IsNullOrWhiteSpace(okta["Authority"])
                     && !string.IsNullOrWhiteSpace(okta["ClientId"]);
var devAuthEnabled = !oktaConfigured && builder.Environment.IsDevelopment();

if (!oktaConfigured && !devAuthEnabled)
{
    throw new InvalidOperationException(
        "Okta OIDC is not configured. Set Okta:Authority, Okta:ClientId and Okta:ClientSecret before running outside Development.");
}

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = oktaConfigured
        ? OpenIdConnectDefaults.AuthenticationScheme
        : CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "GitLabPortal.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/denied";
});

if (oktaConfigured)
{
    authBuilder.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.Authority = okta["Authority"];
        options.ClientId = okta["ClientId"];
        options.ClientSecret = okta["ClientSecret"];
        options.CallbackPath = okta["CallbackPath"] ?? "/authorization-code/callback";
        options.SignedOutCallbackPath = okta["SignedOutCallbackPath"] ?? "/signout/callback";
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "name",
            RoleClaimType = ClaimTypes.Role,
            ValidateIssuer = true
        };
        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = async context =>
            {
                var email = PortalAccessService.GetEmail(context.Principal)?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(email))
                {
                    context.Fail("The identity provider did not return an email claim.");
                    return;
                }

                var factory = context.HttpContext.RequestServices
                    .GetRequiredService<IDbContextFactory<AppDbContext>>();
                await using var db = await factory.CreateDbContextAsync();
                var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
                if (user is null || !user.IsActive)
                {
                    context.Fail("This account is not authorized to use the portal.");
                    return;
                }

                user.LastLoginUtc = DateTime.UtcNow;
                user.DisplayName ??= context.Principal?.Identity?.Name;
                await db.SaveChangesAsync();
            }
        };
    });
}

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(nameof(UserRole.Admin)));
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Static assets must stay reachable for anonymous visitors on the sign-in page.
app.MapStaticAssets().AllowAnonymous();

app.MapGet("/account/login", (string? returnUrl) =>
{
    var redirect = string.IsNullOrWhiteSpace(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
        ? "/"
        : returnUrl;

    return oktaConfigured
        ? Results.Challenge(new AuthenticationProperties { RedirectUri = redirect },
            [OpenIdConnectDefaults.AuthenticationScheme])
        : Results.Redirect($"/account/sign-in?returnUrl={Uri.EscapeDataString(redirect)}");
}).AllowAnonymous();

app.MapPost("/account/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return oktaConfigured
        ? Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
            [OpenIdConnectDefaults.AuthenticationScheme])
        : Results.Redirect("/");
}).RequireAuthorization();

if (devAuthEnabled)
{
    // Development-only shortcut so the portal is usable before Okta is wired up.
    app.MapPost("/account/dev-login", async (HttpContext http, IDbContextFactory<AppDbContext> factory) =>
    {
        var form = await http.Request.ReadFormAsync();
        var email = form["email"].ToString().Trim().ToLowerInvariant();
        var returnUrl = form["returnUrl"].ToString();
        if (string.IsNullOrWhiteSpace(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
            returnUrl = "/";

        await using var db = await factory.CreateDbContextAsync();
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !user.IsActive)
            return Results.Redirect($"/account/sign-in?error=1&returnUrl={Uri.EscapeDataString(returnUrl)}");

        user.LastLoginUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, user.DisplayName ?? user.Email),
            new Claim(ClaimTypes.Email, user.Email)
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Redirect(returnUrl);
    }).AllowAnonymous();
}

app.MapGet("/projects/{projectId:int}/jobs/{jobId:long}/artifacts",
    async (int projectId, long jobId, HttpContext http, PortalAccessService access, GitLabApiService gitlab, CancellationToken ct) =>
    {
        var projectAccess = await access.GetProjectAccessAsync(http.User, projectId, ct);
        if (projectAccess is null) return Results.Forbid();

        var result = await gitlab.DownloadArtifactsAsync(projectAccess.Project, jobId, ct);
        if (!result.Success || result.Value is null)
            return Results.Problem(result.Error ?? "Artifact download failed.", statusCode: StatusCodes.Status502BadGateway);

        return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }).RequireAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
