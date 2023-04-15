
namespace Revit2Gltf;

public class GltfSettings
{
    public bool UseDraco { get; set; }
    public bool ExportAsGLB { get; set; } = true;
    public bool ExportTextures { get; set; } = true;
    public bool ExportParameters { get; set; }
    public int Quality { get; set; } = -1;
}
