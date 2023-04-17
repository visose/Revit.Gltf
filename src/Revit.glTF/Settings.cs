using Autodesk.Revit.DB;

namespace RevitGltf;

public class Settings
{
    public bool UseDraco { get; set; }
    public bool ExportAsGLB { get; set; } = true;
    public bool ExportTextures { get; set; } = true;
    public bool ExportParameters { get; set; }
    public int Quality { get; set; } = -1;
    public bool BoxInstances { get; set; }
    public ICollection<ElementId>? Elements { get; set; }
}
