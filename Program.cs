using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Text;
using System.Text.Json.Serialization;
using TimeSheet;
using TimeSheet.BackgroundQueue;
using TimeSheet.Models;
using TimeSheet.Repository;
using TimeSheet.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));
//builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql("Host=dpg-d0n1vd2li9vc7380m3o0-a.singapore-postgres.render.com;Database=timesheet_dev;Username=myuser;Password=ODIfyKykuj6zdwchsnqAzccSMNeRgGQ7;Include Error Detail=true;"));
AppContext.SetSwitch("Npgsql.DisableDateTimeInfinityConversions", false);
builder.Services.AddDbContext<AppDbContext>(options =>
options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
//AppContext.SetSwitch("Npgsql.DisableDateTimeInfinityConversions", false);
// allow large uploads
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 104_857_600; // 100 MB
});
builder.Services.Configure<FormOptions>(x =>
{
    x.MultipartBodyLengthLimit = 104_857_600; // 100 MB
});

builder.Services.AddTransient<EmailService>();
builder.Services.AddScoped<IApprovalService, ApprovalService>();
builder.Services.AddScoped<IApprovalActionRepository, ApprovalActionRepository>();
builder.Services.AddScoped<IApprovalApproverRepository, ApprovalApproverRepository>();
builder.Services.AddScoped<IApprovalRequestRepository, ApprovalRequestRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IConfigValueRepository, ConfigValueRepository>();
//builder.Services.AddControllers()
//    .AddJsonOptions(x =>
//        x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Prevent circular reference issues
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;

        // Allow enums as strings in JSON
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });


builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
    };
});


// Register the background queue and hosted service
builder.Services.AddSingleton<IBackgroundTaskQueue>(sp => new BackgroundTaskQueue(capacity: 1000));
builder.Services.AddHostedService<QueuedHostedService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(x => x
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());
//app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
