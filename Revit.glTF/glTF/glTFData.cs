#pragma warning disable IDE1006 // Naming Styles
using Newtonsoft.Json;

namespace Revit2Gltf.glTF;

class GLTF
{
    public glTFVersion asset { get; } = new();
    public List<glTFScene> scenes { get; } = new();
    public List<glTFNode> nodes { get; } = new();
    public List<glTFMesh> meshes { get; } = new();
    public List<glTFBufferView> bufferViews { get; } = new();
    public List<glTFAccessor> accessors { get; } = new();
    public List<glTFBuffer> buffers { get; } = new();
    public List<glTFMaterial> materials { get; } = new();
    public List<glTFCameras>? cameras { get; set; }
    public List<glTFImage>? images { get; set; }
    public List<glTFTexture>? textures { get; set; }
    public List<glTFSampler>? samplers { get; set; }
    public List<string>? extensionsRequired { get; set; }
    public List<string>? extensionsUsed { get; set; }

    public string ToJson()
    {
        string jsonStr = JsonConvert.SerializeObject(this, new JsonSerializerSettings
        {
            //Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        });
        return jsonStr;
    }
}

class GLB
{
    public const uint Magic = 0x46546C67;
    public const uint Version = 2;
    public const uint HeaderLength = sizeof(uint) + sizeof(uint) + sizeof(int);
    public const uint ChunkHeaderLength = sizeof(uint) + sizeof(uint);
    public const uint ChunkFormatJson = 0x4E4F534A;
    public const uint ChunkFormatBin = 0x004E4942;
}

class glTFVersion
{
    public readonly string generator = "exportGLTF by:https://github.com/visose/Revit2GLTF";
    public readonly string version = "2.0";
    public Dictionary<string, object>? extras { get; set; }
    public Dictionary<string, object>? extensions { get; set; }
}

class glTFScene
{
    public List<int> nodes = new();
}

class glTFNode
{
    public string? name { get; set; }
    public int? mesh { get; set; }
    public int? camera { get; set; }
    public IList<float>? rotation { get; set; }
    public IList<float>? translation { get; set; }
    public IList<float>? matrix { get; set; }
    public List<int>? children { get; set; }
    public Dictionary<string, object>? extensions { get; set; }
    public Dictionary<string, object>? extras { get; set; }
}

class glTFParameterGroup
{
    public string? GroupName { get; set; }
    public List<glTFParameter> Parameters { get; set; } = new();
}

class glTFParameter
{
    public string? value { get; set; }
    public string? name { get; set; }
}

class glTFMesh
{
    public List<glTFMeshPrimitive>? primitives { get; set; }
}

class glTFMeshPrimitive
{
    public glTFAttribute attributes { get; set; } = new();
    public int indices { get; set; }
    public int? material { get; set; }
    public ModeEnum mode { get; set; } = ModeEnum.TRIANGLES;
    public glTFPrimitiveExtensions? extensions { get; set; }
}

enum ModeEnum
{
    POINTS = 0,
    LINES = 1,
    LINE_LOOP = 2,
    LINE_STRIP = 3,
    TRIANGLES = 4,
    TRIANGLE_STRIP = 5,
    TRIANGLE_FAN = 6
}

class glTFAttribute
{
    public int? POSITION { get; set; }
    public int? NORMAL { get; set; }
    public int? TEXCOORD_0 { get; set; }
    public int? _BATCHID { get; set; }
}

class glTFPrimitiveExtensions
{
    public glTFDracoMesh KHR_draco_mesh_compression { get; set; } = new();
}

class glTFDracoMesh
{
    public int? bufferView { get; set; }
    public glTFAttribute attributes { get; set; } = new();
}

class glTFBuffer
{
    public string? uri { get; set; }
    public int byteLength { get; set; }
}

[JsonObject(MemberSerialization.OptOut)]
class glTFBufferView
{
    public string? name { get; set; }
    public int buffer { get; set; }
    public int byteOffset { get; set; }
    public int byteLength { get; set; }
    public Targets? target { get; set; }
    public int? byteStride { get; set; }

    [JsonIgnore]
    public string? Base64 { get; set; }
}

enum Targets
{
    ARRAY_BUFFER = 34962, // 代表顶点数据
    ELEMENT_ARRAY_BUFFER = 34963 // 代表顶点索引数据
}

class glTFAccessor
{
    public string? name { get; set; }
    public int? bufferView { get; set; }
    public int? byteOffset { get; set; }
    public ComponentType componentType { get; set; }
    public int count { get; set; }
    public string? type { get; set; }
    public IList<float>? max { get; set; }
    public IList<float>? min { get; set; }
}

class AccessorType
{
    public const string VEC3 = "VEC3";
    public const string VEC2 = "VEC2";
    public const string SCALAR = "SCALAR";
}

enum ComponentType
{
    BYTE = 5120,
    UNSIGNED_BYTE = 5121,
    SHORT = 5122,
    UNSIGNED_SHORT = 5123,
    UNSIGNED_INT = 5125,
    FLOAT = 5126
}

class glTFMaterial
{
    public string? name { get; set; }
    public glTFPBR? pbrMetallicRoughness { get; set; }
    public string? alphaMode { get; set; }
    public bool? doubleSided { get; set; }

    [JsonIgnore]
    public int index { get; set; }
}

class glTFPBR
{
    public glTFbaseColorTexture? baseColorTexture { get; set; }
    public List<float>? baseColorFactor { get; set; }
    //Metalness, ranging from 0 (non-metal) to 1 (metal)
    public float? metallicFactor { get; set; }
    //Roughness, ranging from 0.0 (smooth) to 1.0 (rough).
    public float? roughnessFactor { get; set; }
}

class glTFbaseColorTexture
{
    public int? index { get; set; }
}

class glTFTexture
{
    public int? source { get; set; }
    public int? sampler { get; set; }
}

class glTFImage
{

    public string? uri { get; set; }

    public int? bufferView { get; set; }

    public string? mimeType { get; set; }

    public string? name { get; set; }
}

class glTFSampler
{
    public float magFilter { get; set; }
    public float minFilter { get; set; }
    public float wrapS { get; set; }
    public float wrapT { get; set; }
}

class glTFCameras
{
    public string? type { get; set; }
    public glTFPerspectiveCamera? perspective { get; set; }
    public glTFOrthographicCamera? orthographic { get; set; }
}

class CameraType
{
    public const string perspective = "perspective";
    public const string orthographic = "orthographic";
}

class glTFPerspectiveCamera
{
    public float aspectRatio { get; set; }
    public float yfov { get; set; }
    public float zfar { get; set; }
    public float znear { get; set; }
}

class glTFOrthographicCamera
{
    public float xmag { get; set; }
    public float ymag { get; set; }
    public float zfar { get; set; }
    public float znear { get; set; }
}

class glTFBinaryData
{
    public List<float> vertexBuffer { get; set; } = new();
    public List<float> normalBuffer { get; set; } = new();
    public List<int> indexBuffer { get; set; } = new();
    public List<float> uvBuffer { get; set; } = new();
    public List<int> batchidBuffer { get; set; } = new();
    public int? indexMax { get; set; }
    public int? indexAlign { get; set; }
    public IntPtr dracoData { get; set; }
    public int dracoSize { get; set; }
}
