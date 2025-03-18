using DriverLicenseAPI.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.Xml.Serialization;
using Tesseract;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace DriverLicenseAPI.Services;

public class DriverLicenseOcrService
{
    private readonly ILogger<DriverLicenseOcrService> _logger;
    private readonly string _templatesDirectory;
    private readonly string _tessdataPath;

    public DriverLicenseOcrService(ILogger<DriverLicenseOcrService> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _templatesDirectory = Path.Combine(env.ContentRootPath, "Templates");
        _tessdataPath = Path.Combine(env.ContentRootPath, "tessdata");

        // Ensure the Tessdata directory exists
        if (!Directory.Exists(_tessdataPath))
        {
            _logger.LogWarning("Tessdata directory not found! Creating it at: {path}", _tessdataPath);
            Directory.CreateDirectory(_tessdataPath);
        }
    }

    public async Task<DriverLicenseData?> ExtractLicenseDataAsync(string state, byte[] imageBytes)
    {
        try
        {
            // Normalize state name for file matching
            state = state.ToLower().Replace(" ", "");
            
            // Get the template for the detected state
            var template = await LoadTemplateAsync(state);
            if (template == null)
            {
                _logger.LogWarning("No template found for state: {state}", state);
                return null;
            }

            // Convert the image bytes to a bitmap
            using var memoryStream = new MemoryStream(imageBytes);
            using var image = Image.FromStream(memoryStream);
            
            // Extract text from the image based on the template
            var licenseData = new DriverLicenseData
            {
                State = state
            };

            try
            {
                // Initialize Tesseract OCR engine - make sure to download language data
                // See: https://github.com/tesseract-ocr/tessdata
                using var engine = new TesseractEngine(_tessdataPath, "eng", EngineMode.Default);
                
                if (template.Objects != null)
                {
                    foreach (var field in template.Objects)
                    {
                        if (field.Name == null || field.BoundingBox == null)
                            continue;
                        
                        // Extract the field area from the image
                        var rect = new Rectangle(
                            field.BoundingBox.XMin, 
                            field.BoundingBox.YMin,
                            field.BoundingBox.XMax - field.BoundingBox.XMin,
                            field.BoundingBox.YMax - field.BoundingBox.YMin);
                            
                        // Check if the rectangle is within image bounds
                        if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 ||
                            rect.X + rect.Width > image.Width || rect.Y + rect.Height > image.Height)
                        {
                            _logger.LogWarning("Field {fieldName} has invalid bounds", field.Name);
                            continue;
                        }
                        
                        // Extract the field region
                        using var fieldImage = new Bitmap(rect.Width, rect.Height);
                        using var graphics = Graphics.FromImage(fieldImage);
                        
                        graphics.DrawImage(image, 
                            new Rectangle(0, 0, rect.Width, rect.Height),
                            rect, 
                            GraphicsUnit.Pixel);
                        
                        // Use OCR to extract text
                        using var tempStream = new MemoryStream();
                        fieldImage.Save(tempStream, ImageFormat.Png);
                        tempStream.Position = 0;
                        
                        using var pix = Pix.LoadFromMemory(tempStream.ToArray());
                        using var page = engine.Process(pix);
                        
                        var text = page.GetText().Trim();
                        licenseData.Fields[field.Name] = text;
                        
                        _logger.LogInformation("Field {fieldName}: {text}", field.Name, text);
                    }
                }
                
                return licenseData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing OCR on license image");
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting license data");
            return null;
        }
    }

    private async Task<LicenseTemplate?> LoadTemplateAsync(string state)
    {
        try
        {
            _logger.LogInformation("Looking for template for state: {state}", state);
            
            // First try in the States subdirectory
            var templatePath = Path.Combine(_templatesDirectory, "States", $"{state}.xml");
            _logger.LogInformation("Checking template path: {path}", templatePath);
            
            if (!File.Exists(templatePath))
            {
                _logger.LogInformation("Template not found in States directory, checking direct path");
                
                // Fallback to direct Templates directory
                templatePath = Path.Combine(_templatesDirectory, $"{state}.xml");
                _logger.LogInformation("Checking fallback template path: {path}", templatePath);
                
                if (!File.Exists(templatePath))
                {
                    _logger.LogWarning("Template file not found in either location for state: {state}", state);
                    return null;
                }
            }

            _logger.LogInformation("Template found at: {path}", templatePath);
            using var fileStream = new FileStream(templatePath, FileMode.Open);
            var serializer = new XmlSerializer(typeof(LicenseTemplate));
            var template = serializer.Deserialize(fileStream) as LicenseTemplate;
            
            if (template != null)
            {
                _logger.LogInformation("Template loaded successfully for state: {state}", state);
                if (template.Objects != null)
                {
                    _logger.LogInformation("Template contains {count} field definitions", template.Objects.Count);
                }
            }
            
            return template;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading template for state: {state}", state);
            return null;
        }
    }
} 