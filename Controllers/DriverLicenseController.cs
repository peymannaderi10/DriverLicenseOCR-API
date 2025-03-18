using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DriverLicenseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DriverLicenseController : ControllerBase
{
    private readonly ILogger<DriverLicenseController> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _ollamaEndpoint = "http://localhost:11434";
    private readonly string _modelName = "llava:7b-v1.6-mistral-q2_K"; 

    public DriverLicenseController(ILogger<DriverLicenseController> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        
        // Get a custom HttpClient with a long timeout
        _httpClient = httpClientFactory.CreateClient("OllamaClient");
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> AnalyzeDriverLicense(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Please upload a valid image file.");

        try
        {
            // Convert the image to base64
            string base64Image;
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                base64Image = Convert.ToBase64String(memoryStream.ToArray());
            }

            // Create a direct request to Ollama API
            var requestBody = new
            {
                model = _modelName,
                prompt = "CONCISE TASK: Identify ONLY the US state name (usually says at the top left) from this driver's license. Response format: just the state name and the expiration date.",
                images = new[] { base64Image },
                stream = false, // Don't stream to simplify processing
                options = new
                {
                    temperature = 0.0,   // Zero temperature for deterministic output
                    num_predict = 10,    // Limit token generation
                    top_p = 0.1,         // Restrict to only the most likely tokens
                    top_k = 1,            // Consider only the most likely token
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            // Send the request
            var response = await _httpClient.PostAsync($"{_ollamaEndpoint}/api/generate", content);
            response.EnsureSuccessStatusCode();

            // Parse the response
            var responseBody = await response.Content.ReadAsStringAsync();
            var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseBody);
            
            // Extract and post-process the response
            string analysis = ollamaResponse?.Response?.Trim() ?? "Unknown";
            
            // Extract just the state name if explanation is still provided
            if (analysis.Length > 20)
            {
                // Look for state names in the response
                var stateNames = new[] { "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", 
                    "Connecticut", "Delaware", "Florida", "Georgia", "Hawaii", "Idaho", "Illinois", "Indiana", 
                    "Iowa", "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan", 
                    "Minnesota", "Mississippi", "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", 
                    "New Jersey", "New Mexico", "New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma", 
                    "Oregon", "Pennsylvania", "Rhode Island", "South Carolina", "South Dakota", "Tennessee", 
                    "Texas", "Utah", "Vermont", "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming" };
                
                foreach (var state in stateNames)
                {
                    if (analysis.Contains(state, StringComparison.OrdinalIgnoreCase))
                    {
                        analysis = state;
                        break;
                    }
                }
                
                // If we still have a long response and no state found, return "Unknown"
                if (analysis.Length > 20)
                {
                    analysis = "Unknown";
                }
            }

            return Ok(new { analysis });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing driver's license image");
            return StatusCode(500, $"Error analyzing image: {ex.Message}");
        }
    }
    
    // Simple class to parse Ollama API response
    private class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }
    }
} 