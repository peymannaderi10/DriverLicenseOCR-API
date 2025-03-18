using System.Xml.Serialization;

namespace DriverLicenseAPI.Models;

[XmlRoot("annotation")]
public class LicenseTemplate
{
    [XmlElement("folder")]
    public string? Folder { get; set; }

    [XmlElement("filename")]
    public string? Filename { get; set; }

    [XmlElement("path")]
    public string? Path { get; set; }

    [XmlElement("source")]
    public Source? Source { get; set; }

    [XmlElement("size")]
    public Size? Size { get; set; }

    [XmlElement("segmented")]
    public int Segmented { get; set; }

    [XmlElement("object")]
    public List<LicenseField>? Objects { get; set; }
}

public class Source
{
    [XmlElement("database")]
    public string? Database { get; set; }
}

public class Size
{
    [XmlElement("width")]
    public int Width { get; set; }

    [XmlElement("height")]
    public int Height { get; set; }

    [XmlElement("depth")]
    public int Depth { get; set; }
}

public class LicenseField
{
    [XmlElement("name")]
    public string? Name { get; set; }

    [XmlElement("pose")]
    public string? Pose { get; set; }

    [XmlElement("truncated")]
    public int Truncated { get; set; }

    [XmlElement("difficult")]
    public int Difficult { get; set; }

    [XmlElement("bndbox")]
    public BoundingBox? BoundingBox { get; set; }
}

public class BoundingBox
{
    [XmlElement("xmin")]
    public int XMin { get; set; }

    [XmlElement("ymin")]
    public int YMin { get; set; }

    [XmlElement("xmax")]
    public int XMax { get; set; }

    [XmlElement("ymax")]
    public int YMax { get; set; }
}

public class DriverLicenseData
{
    public string State { get; set; } = string.Empty;
    public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();
} 