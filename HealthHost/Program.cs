var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

var health = () => new
{
    service = "thinggateway",
    instance = Environment.MachineName,
    serviceStatus = "Healthy",
    trafficStatus = "Allowed",
    application = new { status = "Healthy", alive = true, checkedAt = DateTimeOffset.UtcNow },
    dependencies = Array.Empty<object>(),
    reasons = Array.Empty<object>(),
    checkedAt = DateTimeOffset.UtcNow
};

app.MapGet("/health/live", () => Results.Ok(health()));
app.MapGet("/health/ready", () => Results.Ok(health()));
app.MapGet("/health/dependencies", () => Results.Ok(health()));
app.MapGet("/health/traffic", () => Results.Ok(health()));
app.MapGet("/healthz", () => Results.Ok(new { status = "Healthy", service = "thinggateway" }));
app.MapGet("/", () => Results.Ok(new { service = "thinggateway", status = "Healthy" }));

app.Run();
