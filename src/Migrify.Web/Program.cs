using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Migrify.Core.Interfaces;
using Migrify.Infrastructure;
using Migrify.Infrastructure.Data;
using Migrify.Web.Components;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Load local overrides (gitignored, for dev credentials)
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// File logging to logs/ folder
var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logsDir);
builder.Logging.AddSimpleConsole();
builder.Logging.AddFile(Path.Combine(logsDir, "migrify-{Date}.log"));

// Infrastructure services (DbContext, repositories, encryption, connection testers)
builder.Services.AddInfrastructure(builder.Configuration);

// ASP.NET Identity
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.LogoutPath = "/account/logout";
    options.ExpireTimeSpan = TimeSpan.FromHours(24);
});

// MudBlazor
builder.Services.AddMudServices();

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// Database migratie + admin seed bij opstarten
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var adminEmail = app.Configuration["AdminUser:Email"] ?? "admin@migrify.local";
    var adminPassword = app.Configuration["AdminUser:Password"] ?? "Admin123!";

    if (await userManager.FindByEmailAsync(adminEmail) is null)
    {
        var admin = new IdentityUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };
        await userManager.CreateAsync(admin, adminPassword);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

// Login POST endpoint
app.MapPost("/api/account/login", async (
    HttpContext context,
    SignInManager<IdentityUser> signInManager,
    UserManager<IdentityUser> userManager) =>
{
    var form = await context.Request.ReadFormAsync();
    var email = form["_loginModel.Email"].ToString();
    var password = form["_loginModel.Password"].ToString();

    var user = await userManager.FindByEmailAsync(email);
    if (user is not null)
    {
        var result = await signInManager.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            return Results.Redirect("/");
        }
    }

    return Results.Redirect("/account/login?error=invalid");
});

// Logout POST endpoint
app.MapPost("/api/account/logout", async (SignInManager<IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/account/login");
});

// Google OAuth2 callback endpoint
app.MapGet("/oauth/callback/google", async (
    HttpContext context,
    IOAuthTokenService oauthService,
    IProjectRepository projectRepository,
    ICredentialEncryptor encryptor,
    ILogger<Program> logger) =>
{
    var code = context.Request.Query["code"].ToString();
    var state = context.Request.Query["state"].ToString();
    var error = context.Request.Query["error"].ToString();

    if (!string.IsNullOrEmpty(error))
    {
        logger.LogWarning("OAuth callback received error: {Error}", error);
        return Results.Content(BuildOAuthCallbackHtml(false, "", $"Google returned an error: {error}"), "text/html");
    }

    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
    {
        return Results.Content(BuildOAuthCallbackHtml(false, "", "Missing authorization code or state parameter."), "text/html");
    }

    try
    {
        // Decode state → projectId
        var projectIdStr = Encoding.UTF8.GetString(Convert.FromBase64String(state));
        if (!Guid.TryParse(projectIdStr, out var projectId))
        {
            return Results.Content(BuildOAuthCallbackHtml(false, "", "Invalid state parameter."), "text/html");
        }

        var project = await projectRepository.GetByIdAsync(projectId);
        if (project?.ImapSettings is null)
        {
            return Results.Content(BuildOAuthCallbackHtml(false, "", "Project not found."), "text/html");
        }

        var imap = project.ImapSettings;
        if (string.IsNullOrEmpty(imap.OAuthClientId) || string.IsNullOrEmpty(imap.EncryptedOAuthClientSecret))
        {
            return Results.Content(BuildOAuthCallbackHtml(false, "", "OAuth2 credentials not configured for this project."), "text/html");
        }

        var clientId = imap.OAuthClientId;
        var clientSecret = encryptor.Decrypt(imap.EncryptedOAuthClientSecret);
        var redirectUri = $"{context.Request.Scheme}://{context.Request.Host}/oauth/callback/google";

        var result = await oauthService.ExchangeCodeAsync(code, clientId, clientSecret, redirectUri);

        // Store tokens encrypted
        imap.EncryptedOAuthAccessToken = encryptor.Encrypt(result.AccessToken);
        imap.EncryptedOAuthRefreshToken = encryptor.Encrypt(result.RefreshToken);
        imap.OAuthTokenExpiresAtUtc = result.ExpiresAtUtc;
        imap.OAuthProvider = "Google";
        await projectRepository.UpdateAsync(project);

        logger.LogInformation("OAuth2 tokens stored for project {ProjectId}", projectId);
        return Results.Content(BuildOAuthCallbackHtml(true, projectId.ToString(), ""), "text/html");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "OAuth callback failed");
        return Results.Content(BuildOAuthCallbackHtml(false, "", ex.Message), "text/html");
    }
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string BuildOAuthCallbackHtml(bool success, string projectId, string error)
{
    var encodedError = System.Net.WebUtility.HtmlEncode(error).Replace("'", "\\'");
    var message = success
        ? "<h3>Authorization successful!</h3><p>This window will close automatically.</p>"
        : "<h3>Authorization failed</h3><p>" + System.Net.WebUtility.HtmlEncode(error) + "</p><p>You can close this window and try again.</p>";
    var successJs = success ? "true" : "false";
    var autoClose = success ? "setTimeout(function() { window.close(); }, 2000);" : "";

    return "<!DOCTYPE html><html><head><title>Migrify - OAuth2</title>"
        + "<style>body { font-family: sans-serif; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; background: #1a1a2e; color: #fff; } .box { text-align: center; }</style>"
        + "</head><body><div class=\"box\">" + message + "</div>"
        + "<script>"
        + "if (window.opener) {"
        + "  window.opener.postMessage({"
        + "    type: 'oauth-callback',"
        + "    success: " + successJs + ","
        + "    projectId: '" + projectId + "',"
        + "    error: '" + encodedError + "'"
        + "  }, '*');"
        + "}"
        + autoClose
        + "</script></body></html>";
}
