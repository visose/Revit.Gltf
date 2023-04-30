using Autodesk.Revit.DB;

namespace RevitGltf;

public class Settings
{
    public bool UseDraco { get; set; }
    public bool ExportAsGlb { get; set; } = true;
    public bool ExportTextures { get; set; } = true;
    public bool ExportCamera { get; set; } = true;
    public int Quality { get; set; } = -1;
    public bool BoxInstances { get; set; }
    public ICollection<ElementId>? Elements { get; set; }
    public ICustomExporter? CustomExporter { get; set; }
}

public interface ICustomExporter
{
    void Mutate(Gltf gltf);
    string? SetMaterialName(MaterialNode node);
}
