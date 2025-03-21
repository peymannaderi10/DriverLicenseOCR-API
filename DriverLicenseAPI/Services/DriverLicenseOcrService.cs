using DriverLicenseAPI.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Xml.Serialization;
using Tesseract;
using ImageFormat = System.Drawing.Imaging.ImageFormat;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;

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
        
        // Verify platform compatibility for System.Drawing
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogWarning("System.Drawing is only fully supported on Windows. Some image processing features may not work correctly on this platform.");
        }
    }

    [SupportedOSPlatform("windows")]
    public async Task<(DriverLicenseData? LicenseData, string? ProcessedImageBase64)> ExtractLicenseDataAsync(string state, byte[] imageBytes)
    {
        try
        {
            // Normalize state name for file matching
            state = state.ToLower().Replace(" ", "");
            
            // Get the template for the detected state
            var template = LoadTemplateAsync(state);
            if (template == null)
            {
                _logger.LogWarning("No template found for state: {state}", state);
                return (null, null);
            }

            // Convert the image bytes to a bitmap and preprocess it
            using var memoryStream = new MemoryStream(imageBytes);
            using var originalImage = Image.FromStream(memoryStream);
            
            // Preprocess the image - convert to grayscale for better OCR
            using var grayscaleImage = ConvertToGrayscale((Bitmap)originalImage);
            
            _logger.LogInformation("Converted image to grayscale for improved OCR");
            
            // Save the grayscale image to return to client
            string processedImageBase64;
            using (var msProcessed = new MemoryStream())
            {
                grayscaleImage.Save(msProcessed, ImageFormat.Jpeg);
                processedImageBase64 = Convert.ToBase64String(msProcessed.ToArray());
            }
            
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
                
                // Configure engine for license text
                engine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-./");
                
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
                            rect.X + rect.Width > grayscaleImage.Width || rect.Y + rect.Height > grayscaleImage.Height)
                        {
                            _logger.LogWarning("Field {fieldName} has invalid bounds", field.Name);
                            continue;
                        }
                        
                        // Extract the field region
                        using var fieldImage = new Bitmap(rect.Width, rect.Height);
                        using var graphics = Graphics.FromImage(fieldImage);
                        
                        graphics.DrawImage(grayscaleImage, 
                            new Rectangle(0, 0, rect.Width, rect.Height),
                            rect, 
                            GraphicsUnit.Pixel);
                        
                        // Further enhance the field image for OCR
                        using var enhancedFieldImage = EnhanceImageForOcr(fieldImage);
                        
                        // Use OCR to extract text
                        using var tempStream = new MemoryStream();
                        enhancedFieldImage.Save(tempStream, ImageFormat.Png);
                        tempStream.Position = 0;
                        
                        using var pix = Pix.LoadFromMemory(tempStream.ToArray());
                        using var page = engine.Process(pix);
                        
                        var text = page.GetText().Trim();
                        
                        // Special processing for the Sex field - extract only F or M
                        if (field.Name.Equals("Sex", StringComparison.OrdinalIgnoreCase))
                        {
                            // Find the first F or M in the text
                            var match = text.ToUpper().FirstOrDefault(c => c == 'F' || c == 'M');
                            if (match != '\0')
                            {
                                text = match.ToString();
                            }
                        }
                        // Special processing for Address fields
                        else if (field.Name.Contains("Address", StringComparison.OrdinalIgnoreCase))
                        {
                            text = FormatAddress(text);
                        }
                        
                        licenseData.Fields[field.Name] = text;
                        
                        _logger.LogInformation("Field {fieldName}: {text}", field.Name, text);
                    }
                }
                
                return (licenseData, processedImageBase64);
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
            return (null, null);
        }
    }

    [SupportedOSPlatform("windows")]
    private Bitmap ConvertToGrayscale(Bitmap original)
    {
        // Create a blank bitmap the same size as original
        Bitmap grayscale = new Bitmap(original.Width, original.Height);

        // Get a graphics object from the new image
        using Graphics g = Graphics.FromImage(grayscale);

        // Create grayscale color matrix
        ColorMatrix colorMatrix = new ColorMatrix(
            new float[][]
            {
                new float[] {0.3f, 0.3f, 0.3f, 0, 0},
                new float[] {0.59f, 0.59f, 0.59f, 0, 0},
                new float[] {0.11f, 0.11f, 0.11f, 0, 0},
                new float[] {0, 0, 0, 1, 0},
                new float[] {0, 0, 0, 0, 1}
            });

        // Create image attributes
        using ImageAttributes attributes = new ImageAttributes();
        
        // Set the color matrix
        attributes.SetColorMatrix(colorMatrix);

        // Draw the original image on the new image using the grayscale color matrix
        g.DrawImage(original, 
            new Rectangle(0, 0, original.Width, original.Height),
            0, 0, original.Width, original.Height, 
            GraphicsUnit.Pixel, attributes);

        return grayscale;
    }

    [SupportedOSPlatform("windows")]
    private Bitmap EnhanceImageForOcr(Bitmap image)
    {
        // Create a new bitmap for the enhanced image
        Bitmap enhancedImage = new Bitmap(image.Width, image.Height);
        
        // Apply contrast enhancement and other processing
        using (Graphics g = Graphics.FromImage(enhancedImage))
        {
            // Create a high contrast color matrix
            ColorMatrix colorMatrix = new ColorMatrix(
                new float[][]
                {
                    new float[] {2.0f, 0, 0, 0, 0},
                    new float[] {0, 2.0f, 0, 0, 0},
                    new float[] {0, 0, 2.0f, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {-0.5f, -0.5f, -0.5f, 0, 1}
                });

            // Create image attributes
            using ImageAttributes attributes = new ImageAttributes();
            
            // Set the color matrix
            attributes.SetColorMatrix(colorMatrix);
            
            // Draw the original image on the new image using the high contrast color matrix
            g.DrawImage(image, 
                new Rectangle(0, 0, image.Width, image.Height),
                0, 0, image.Width, image.Height, 
                GraphicsUnit.Pixel, attributes);
        }
        
        return enhancedImage;
    }

    private LicenseTemplate? LoadTemplateAsync(string state)
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

    // Method to format address string into proper format
    private string FormatAddress(string rawAddress)
    {
        try
        {
            rawAddress = rawAddress.Trim();
            if (string.IsNullOrEmpty(rawAddress))
                return rawAddress;

            // Remove periods but keep hyphens for ZIP+4
            rawAddress = rawAddress.Replace(".", "").Replace(",", "");
            
            // Look for ZIP code pattern (5 digits with optional -4 digits at the end)
            var zipMatch = System.Text.RegularExpressions.Regex.Match(rawAddress, @"(\d{5}(?:-\d{4})?)$");
            string zip = string.Empty;
            
            if (zipMatch.Success)
            {
                zip = zipMatch.Groups[1].Value;
                // Remove ZIP from raw address for further processing
                rawAddress = rawAddress.Substring(0, rawAddress.Length - zip.Length);
            }
            
            // Look for state code (typically 2 uppercase letters before ZIP)
            string state = string.Empty;
            if (rawAddress.Length >= 2)
            {
                state = rawAddress.Substring(rawAddress.Length - 2);
                if (state.Length == 2 && state.All(char.IsLetter))
                {
                    // Remove state from raw address
                    rawAddress = rawAddress.Substring(0, rawAddress.Length - 2);
                }
                else
                {
                    state = string.Empty; // Reset if not valid
                }
            }
            
            // Look for apartment number (like APT2 or APT 2)
            string aptNumber = string.Empty;
            var aptMatch = System.Text.RegularExpressions.Regex.Match(rawAddress, @"APT\s*(\d+)|#\s*(\d+)|\b[A-Z]*\s*UNIT\s*(\d+)", RegexOptions.IgnoreCase);
            
            if (aptMatch.Success)
            {
                // Find which group captured the number
                string number = aptMatch.Groups[1].Value;
                if (string.IsNullOrEmpty(number)) number = aptMatch.Groups[2].Value;
                if (string.IsNullOrEmpty(number)) number = aptMatch.Groups[3].Value;
                
                aptNumber = number;
                
                // Remove apartment info from address
                int index = aptMatch.Index;
                int length = aptMatch.Length;
                
                if (index + length <= rawAddress.Length)
                {
                    rawAddress = rawAddress.Remove(index, length);
                }
            }
            
            // Now split the beginning for house number and street
            var numberMatch = System.Text.RegularExpressions.Regex.Match(rawAddress.Trim(), @"^(\d+)");
            string houseNumber = string.Empty;
            string street = rawAddress.Trim();
            
            if (numberMatch.Success)
            {
                houseNumber = numberMatch.Groups[1].Value;
                street = rawAddress.Substring(houseNumber.Length).Trim();
                
                // Handle special case where street number might contain the ordinal number
                // Example: "257024THSTREET" should be "2570 24TH STREET"
                if (houseNumber.Length > 4 && 
                    (street.StartsWith("TH", StringComparison.OrdinalIgnoreCase) || 
                     street.StartsWith("ND", StringComparison.OrdinalIgnoreCase) || 
                     street.StartsWith("RD", StringComparison.OrdinalIgnoreCase) ||
                     street.StartsWith("ST", StringComparison.OrdinalIgnoreCase)))
                {
                    // Extract the base address number (first 4 digits or fewer)
                    string baseNumber = houseNumber.Substring(0, Math.Min(4, houseNumber.Length));
                    
                    // Extract the ordinal number (remaining digits)
                    string ordinalNumber = houseNumber.Substring(Math.Min(4, houseNumber.Length));
                    
                    // Update house number and street
                    houseNumber = baseNumber;
                    street = ordinalNumber + street;
                    
                    // Add a space after the ordinal indicator
                    if (street.StartsWith("TH", StringComparison.OrdinalIgnoreCase))
                        street = street.Insert(2, " ");
                    else if (street.StartsWith("ND", StringComparison.OrdinalIgnoreCase))
                        street = street.Insert(2, " ");
                    else if (street.StartsWith("RD", StringComparison.OrdinalIgnoreCase))
                        street = street.Insert(2, " ");
                    else if (street.StartsWith("ST", StringComparison.OrdinalIgnoreCase))
                        street = street.Insert(2, " ");
                    
                    _logger.LogInformation("Corrected address format: {baseNumber} {ordinalStreet}", 
                        baseNumber, street);
                }
            }
            
            // Format the result - use spaces between components
            var formattedParts = new List<string>();
            
            if (!string.IsNullOrEmpty(houseNumber))
                formattedParts.Add(houseNumber);
                
            if (!string.IsNullOrEmpty(street))
                formattedParts.Add(street.Trim());
                
            if (!string.IsNullOrEmpty(aptNumber))
                formattedParts.Add("APT " + aptNumber);
                
            // If we couldn't parse a city, it might be part of the street still
            
            if (!string.IsNullOrEmpty(state))
                formattedParts.Add(state);
                
            if (!string.IsNullOrEmpty(zip))
                formattedParts.Add(zip);
                
            return string.Join(" ", formattedParts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error formatting address: {error}. Returning raw text.", ex.Message);
            return rawAddress;
        }
    }
} 