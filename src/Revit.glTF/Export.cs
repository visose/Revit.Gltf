using Autodesk.Revit.DB;

namespace RevitGltf;

public static class Export
{
    public static byte[] ToBytes(View3D view, Settings? settings = null)
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
