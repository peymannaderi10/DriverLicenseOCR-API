using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.IIS;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using DriverLicenseAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register our OCR service
builder.Services.AddSingleton<DriverLicenseOcrService>();

// Configure file upload size limit (if needed for larger image files)
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 20 * 1024 * 1024; // 20 MB
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 20 * 1024 * 1024; // 20 MB
});

// Register HttpClient with custom timeout for Ollama
builder.Services.AddHttpClient("OllamaClient", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10); // 10-minute timeout
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthorization();

app.MapControllers();

// Redirect root to index.html
app.MapGet("/", context => {
    context.Response.Redirect("/index.html");
    return Task.CompletedTask;
});

// Pre-warm the model when application starts
#pragma warning disable CS4014 // Because this call is not awaited
Task.Run(async () => {
    try
    {
        // Wait for the application to fully start
        await Task.Delay(5000);
        
        using var scope = app.Services.CreateScope();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("OllamaClient");
        
        Console.WriteLine("Pre-warming Ollama model...");
        
        // Simple ping to ensure Ollama server is running
        try {
            var pingResponse = await httpClient.GetAsync("http://localhost:11434/api/tags");
            if (!pingResponse.IsSuccessStatusCode) {
                Console.WriteLine($"Ollama server not responding: {pingResponse.StatusCode}");
                return;
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"Ollama server connection error: {ex.Message}");
            return;
        }
        
        // Use a very simple request to load the model into memory
        var requestBody = new
        {
            model = "llava:7b",
            prompt = "What state is this?",
            stream = false,
            options = new
            {
                temperature = 0.0,
                num_predict = 5
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await httpClient.PostAsync("http://localhost:11434/api/generate", content);
        
        Console.WriteLine($"Model pre-warming complete: {response.StatusCode}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error during model pre-warming: {ex.Message}");
    }
});
#pragma warning restore CS4014

app.Run();
