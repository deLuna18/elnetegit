using Microsoft.EntityFrameworkCore;
using SubdivisionManagement.Model;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

builder.Services.AddDbContext<HomeContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlServerOptionsAction: sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<HomeContext>();
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
        context.Database.Migrate();
        
        if (!context.Admins.Any())
        {
            context.Admins.Add(new Admin("admin", HashPassword("password123")));
            context.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while creating/migrating the database.");
    }
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();
app.MapControllers();
app.MapDefaultControllerRoute();

app.MapControllerRoute(
    name: "homeowner",
    pattern: "Homeowner/{action=Login}/{id?}",
    defaults: new { controller = "Homeowner", action = "Login" });

app.MapControllerRoute(
    name: "staff",
    pattern: "Staff/{action=Login}/{id?}",
    defaults: new { controller = "Staff", action = "Login" });

app.MapControllerRoute(
    name: "admin",
    pattern: "Admin/{action=Login}/{id?}",
    defaults: new { controller = "Admin", action = "Login" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Homeowner}/{action=Dashboard}/{id?}");

app.MapControllerRoute(
    name: "admin_facility_reservation",
    pattern: "Admin/FacilityReservation",
    defaults: new { controller = "Admin", action = "AdminFacilityReservation" });

app.UseStaticFiles(new StaticFileOptions
{
    ServeUnknownFileTypes = true,
    DefaultContentType = "text/plain"
});

static string HashPassword(string password)
{
    byte[] salt = Encoding.UTF8.GetBytes("password123");
    return Convert.ToBase64String(KeyDerivation.Pbkdf2(
        password: password,
        salt: salt,
        prf: KeyDerivationPrf.HMACSHA256,
        iterationCount: 10000,
        numBytesRequested: 256 / 8));
}

app.Run();

