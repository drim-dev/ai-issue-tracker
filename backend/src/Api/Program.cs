using System.Reflection;
using Api.Common.Auth;
using Api.Common.Exceptions;
using Api.Common.Http;
using Api.Common.Identity;
using Api.Common.Persistence;
using Api.Common.Validation;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Persistence — AppDbContext registered through the Aspire PostgreSQL integration.
builder.AddNpgsqlDbContext<AppDbContext>("appdb");

// Distributed, time-ordered ID generation.
builder.Services.AddIdFactory(builder.Configuration.GetValue<int>("IdGen:GeneratorId"));

// MediatR + FluentValidation pipeline.
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// Authentication — password hashing; identity arrives via the BFF header.
builder.Services.AddAuth();

// Vertical-slice endpoints.
builder.Services.AddEndpoints();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseMiddleware<UserContextMiddleware>();

app.MapDefaultEndpoints();
app.MapEndpoints();

// Apply EF Core migrations on startup.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

namespace Api
{
    // Exposed so the test harness can reference the entry-point assembly.
    public partial class Program;
}
