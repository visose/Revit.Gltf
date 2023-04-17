using Autodesk.Revit.DB;
using Revit2Gltf.glTF;

namespace Revit2Gltf;

public static class Export
{
    public static byte[] ToBytes(View3D view, GltfSettings? settings = null)
    {
        settings ??= new();
        var doc = view.Document;

        GltfExportContext context = new(view, settings);
        CustomExporter exporter = new(doc, context)
        {
            IncludeGeometricObjects = false,
            ShouldStopOnError = true
        };

        exporter.Export(view);
        return context.Data ?? throw new("Data is null");
    }
}
