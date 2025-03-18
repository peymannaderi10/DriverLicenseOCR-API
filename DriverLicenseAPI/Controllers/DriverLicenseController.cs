using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DriverLicenseAPI.Services;

namespace DriverLicenseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DriverLicenseController : ControllerBase
{
    private readonly ILogger<DriverLicenseController> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _ollamaEndpoint = "http://localhost:11434";
    private readonly string _modelName = "llava:7b-v1.6-mistral-q2_K"; 
    private readonly DriverLicenseOcrService _ocrService;

    public DriverLicenseController(
        ILogger<DriverLicenseController> logger, 
        IHttpClientFactory httpClientFactory,
        DriverLicenseOcrService ocrService)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("OllamaClient");
        _ocrService = ocrService;
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> AnalyzeDriverLicense(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Please upload a valid image file.");

        try
        {
            // Convert the image to base64 and raw bytes
            string base64Image;
            byte[] imageBytes;
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                imageBytes = memoryStream.ToArray();
                base64Image = Convert.ToBase64String(imageBytes);
            }

            // Step 1: Detect the state with Ollama
            var state = await DetectStateAsync(base64Image);
            
            if (string.IsNullOrEmpty(state) || state.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new { 
                    analysis = "Could not identify the state for this driver's license.",
                    state = "Unknown",
                    fields = new Dictionary<string, string>()
                });
            }

            // Step 2: Use the state to find the appropriate template and perform OCR
            var licenseData = await _ocrService.ExtractLicenseDataAsync(state, imageBytes);

            if (licenseData == null)
            {
                return Ok(new { 
                    analysis = $"Detected state: {state}, but could not extract field data.",
                    state,
                    fields = new Dictionary<string, string>()
                });
            }

            return Ok(new { 
                analysis = $"Successfully processed {state} driver's license.",
                state = licenseData.State,
                fields = licenseData.Fields
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing driver's license image");
            return StatusCode(500, $"Error analyzing image: {ex.Message}");
        }
    }

    [HttpPost("analyzeWithState")]
    public async Task<IActionResult> AnalyzeDriverLicenseWithState(IFormFile file, [FromQuery] string state)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Please upload a valid image file.");

        if (string.IsNullOrEmpty(state))
            return BadRequest("Please specify a state name.");

        try
        {
            // Convert the image to bytes
            byte[] imageBytes;
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                imageBytes = memoryStream.ToArray();
            }

            // Normalize state name for consistency
            state = state.Trim();
            
            _logger.LogInformation("Processing license for state: {state}", state);
            
            // Use the specified state to find the template and perform OCR
            var licenseData = await _ocrService.ExtractLicenseDataAsync(state, imageBytes);

            if (licenseData == null)
            {
                return Ok(new { 
                    analysis = $"Could not process {state} driver's license. Template may not exist.",
                    state,
                    fields = new Dictionary<string, string>()
                });
            }

            return Ok(new { 
                analysis = $"Successfully processed {state} driver's license.",
                state = licenseData.State,
                fields = licenseData.Fields
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing driver's license with state {state}", state);
            return StatusCode(500, $"Error analyzing image: {ex.Message}");
        }
    }

    private async Task<string> DetectStateAsync(string base64Image)
    {
        try
        {
            // Create a direct request to Ollama API for state detection
            var requestBody = new
            {
                model = _modelName,
                prompt = "IMPORTANT INSTRUCTIONS: You are analyzing a driver's license image. Your response must be ONLY the state name (e.g., 'California', 'New York'). If you cannot identify a specific state, just respond with 'Unknown'. Do not explain your reasoning or provide any other information.",
                images = new[] { base64Image },
                stream = false // Don't stream to simplify processing
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
            string state = ollamaResponse?.Response?.Trim() ?? "Unknown";
            
            // Extract just the state name if explanation is still provided
            if (state.Length > 20)
            {
                // Look for state names in the response
                var stateNames = new[] { "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", 
                    "Connecticut", "Delaware", "Florida", "Georgia", "Hawaii", "Idaho", "Illinois", "Indiana", 
                    "Iowa", "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan", 
                    "Minnesota", "Mississippi", "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", 
                    "New Jersey", "New Mexico", "New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma", 
                    "Oregon", "Pennsylvania", "Rhode Island", "South Carolina", "South Dakota", "Tennessee", 
                    "Texas", "Utah", "Vermont", "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming" };
                
                foreach (var name in stateNames)
                {
                    if (state.Contains(name, StringComparison.OrdinalIgnoreCase))
                    {
                        state = name;
                        break;
                    }
                }
                
                // If we still have a long response and no state found, return "Unknown"
                if (state.Length > 20)
                {
                    state = "Unknown";
                }
            }

            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting state");
            return "Unknown";
        }
    }
    
    // Simple class to parse Ollama API response
    private class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }
    }
} 