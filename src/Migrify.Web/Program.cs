using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Migrify.Infrastructure;
using Migrify.Infrastructure.Data;
using Migrify.Web.Components;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Load local overrides (gitignored, for dev credentials)
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
