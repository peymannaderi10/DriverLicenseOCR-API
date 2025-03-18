# Driver License OCR API

A .NET Core API that analyzes driver's licenses using AI to detect the state and extract text fields using OCR. The system uses Ollama's vision models for state detection and Tesseract OCR for text extraction.

## Features

- State detection using Ollama's LLaVA vision model
- OCR text extraction based on state-specific templates
- Support for all 50 US states
- Simple web UI for testing
- REST API endpoints for integration

## Prerequisites

### 1. Install Ollama

[Ollama](https://ollama.com/) is used for AI-based state detection:

1. Download and install Ollama from the official website: [https://ollama.com/download](https://ollama.com/download)
2. After installation, run Ollama from your applications menu or command line
3. Pull the LLaVA vision model by running the following in your terminal:

```
ollama pull llava:7b
```

### 2. Install .NET 9 SDK

This project requires .NET 9 SDK:

1. Download and install from: [https://dotnet.microsoft.com/download/dotnet/9.0](https://dotnet.microsoft.com/download/dotnet/9.0)

### 3. Tesseract OCR Data

The application will automatically create a `tessdata` directory, but you need to download the English language data:

1. Download the English language data file from GitHub:
```
Invoke-WebRequest -Uri "https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata" -OutFile "DriverLicenseAPI/tessdata/eng.traineddata"
```

## Installation

1. Clone the repository:
```
git clone https://github.com/peymannaderi10/OCR-API.git
cd OCR-API
```

2. Make sure Tesseract data is installed (if you haven't done step 3 from Prerequisites):
```
# Create tessdata directory if it doesn't exist
mkdir -p DriverLicenseAPI/tessdata
# Download English language data
Invoke-WebRequest -Uri "https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata" -OutFile "DriverLicenseAPI/tessdata/eng.traineddata"
```

3. Place your state XML templates in the `DriverLicenseAPI/Templates/States` directory. Each state should have its own XML file (e.g., `michigan.xml`, `california.xml`).

## Running the Application

1. Navigate to the API project directory:
```
cd DriverLicenseAPI
```

2. Start the application:
```
dotnet run
```

3. Access the web UI by opening a browser and navigating to:
```
https://localhost:5001
```
Or the URL displayed in your console.

## API Usage

The API provides two main endpoints:

### 1. Auto-detect state and extract data

**Endpoint:** `POST /api/DriverLicense/analyze`

**Request:** Form-data with `file` containing the driver's license image

**Response:**
```json
{
  "analysis": "Successfully processed michigan driver's license.",
  "state": "michigan",
  "fields": {
    "License Number": "S123-456-789-000",
    "Expiration Date": "2025-06-30",
    "Date of Birth": "1990-01-01",
    "Full Name": "JOHN DOE",
    "Address": "123 MAIN ST, ANYTOWN, MI 12345",
    "Sex": "M"
  }
}
```

### 2. Process with known state

**Endpoint:** `POST /api/DriverLicense/analyzeWithState?state={state}`

**Request:** 
- Form-data with `file` containing the driver's license image
- Query parameter `state` with the name of the state (e.g., `michigan`)

**Response:** Same structure as above

## Template Structure

The system uses XML templates for each state that define the positions of fields on the license:

```xml
<annotation>
  <object>
    <name>License Number</name>
    <bndbox>
      <xmin>180</xmin>
      <ymin>74</ymin>
      <xmax>354</xmax>
      <ymax>95</ymax>
    </bndbox>
  </object>
  <!-- More fields -->
</annotation>
```
## Labeling More License Images:
I use Label Studio to label the fields I want to extract from on a license variant and will use the generate XML to guide tesseract
into extracting the fields and labelling them correctly without picking up noise.

https://github.com/HumanSignal/labelImg

## Troubleshooting

### Common Issues:

1. **Ollama Connection Error**
   - Ensure Ollama is running
   - Check if the model has been downloaded: `ollama list`
   - Try a different model if needed

2. **Template Not Found**
   - Check that the template file exists in `DriverLicenseAPI/Templates/States/{state}.xml`
   - Ensure the filename is lowercase with no spaces
   - Check logs for template path issues

3. **OCR Not Working**
   - Verify the Tesseract data file is in the correct location
   - Check image quality - blurry images may not OCR properly
   - Adjust bounding boxes in the XML template if needed

4. **Timeout Errors**
   - First-time vision model usage can take longer as the model loads
   - Try using a smaller model version: `llava:7b` instead of larger versions 
