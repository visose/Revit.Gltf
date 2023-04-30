using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;

namespace RevitGltf;

class DracoData
{
    public List<BufferView> BufferViews { get; } = new();
    public List<Task> Tasks { get; } = new();
}

class ElementData
{
    public required Element Element { get; init; }
    public string? SymbolId { get; set; }
    public string? MaterialName { get; set; }
    public Dictionary<string, BinaryData> MapBinaryData { get; } = new();
    public List<int> InstanceNodes { get; } = new();
}

class GltfExportContext : IExportContext
{
    public byte[]? Data { get; private set; }

    readonly View3D _view;
    readonly Settings _settings;
    readonly HashSet<ElementId>? _elements;
    readonly string _texturesFolder;

    readonly Stack<Document> _documentStack = new();
    readonly Stack<Transform> _transformStack = new();

    readonly Gltf _gltf = new();
    readonly List<BinaryData> _allBinaryDatas = new();
    readonly Dictionary<string, Material> _mapMaterial = new();
    readonly Dictionary<string, int> _mapSymbolId = new();

    readonly DracoData? _dracoData;
    ElementData? _elementData;

    Document Doc => _documentStack.Peek();
    Transform CurrentTransform => _transformStack.Peek();

    public GltfExportContext(View3D view, Settings settings)
    {
        _view = view;
        _settings = settings;
        _elements = _settings.Elements?.ToHashSet();
        _texturesFolder = @"C:\Program Files (x86)\Common Files\Autodesk Shared\Materials\Textures\";

        _documentStack.Push(view.Document);
        _transformStack.Push(Transform.Identity);

        if (_settings.UseDraco)
        {
            _gltf.extensionsRequired = new() { "KHR_draco_mesh_compression" };
            _gltf.extensionsUsed = new() { "KHR_draco_mesh_compression" };
            _dracoData = new();
        }

        Scene scene = new()
        {
            nodes = new() { 0 }
        };

        _gltf.scenes.Add(scene);

        Node root = new()
        {
            name = "root",
            children = new()
        };

        _gltf.nodes.Add(root);
    }

    public bool Start() => true;

    public void Finish()
    {
        using MemoryStream memoryStream = new();
        using (BinaryWriter writer = new(memoryStream))
        {
            if (_dracoData is not null)
            {
                // wait for thread to finish
                Task.WaitAll(_dracoData.Tasks.ToArray());

                for (int i = 0; i < _allBinaryDatas.Count; i++)
                {
                    var binData = _allBinaryDatas[i];
                    var data = binData.dracoData;
                    var size = binData.dracoSize;
                    unsafe
                    {
                        byte* memBytePtr = (byte*)data.ToPointer();
                        for (int j = 0; j < size; j++)
                        {
                            writer.Write(*(byte*)memBytePtr);
                            memBytePtr += 1;
                        }

                    }
                    // release native memory
                    Draco.deleteDracoData(data);

                    int byteOffset = 0;
                    var bufferViews = _dracoData.BufferViews;

                    if (i > 0)
                        byteOffset = bufferViews[i - 1].byteLength + bufferViews[i - 1].byteOffset;

                    bufferViews[i].byteOffset = byteOffset;
                    bufferViews[i].byteLength = size;
                }

                _gltf.bufferViews.Clear();
                _gltf.bufferViews.AddRange(_dracoData.BufferViews);

                foreach (var accessor in _gltf.accessors)
                {
                    accessor.bufferView = null;
                    accessor.byteOffset = null;
                }

                if (_gltf.images is not null)
                {
                    foreach (var image in _gltf.images)
                    {
                        image.bufferView = _gltf.bufferViews.Count;
                        var bytes = File.ReadAllBytes(image.uri);
                        var byteOffset = _gltf.bufferViews[^1].byteLength + _gltf.bufferViews[^1].byteOffset;
                        var imageView = Util.CreateBufferView(0, byteOffset, bytes.Length);
                        image.uri = null;
                        writer.Write(bytes);
                        _gltf.bufferViews.Add(imageView);
                    }
                }
            }
            else
            {
                foreach (var binData in _allBinaryDatas)
                {
                    foreach (var index in binData.indexBuffer)
                    {
                        if (binData.indexMax > 65535)
                        {
                            writer.Write((uint)index);
                        }
                        else
                        {
                            writer.Write((ushort)index);
                        }
                    }

                    if (binData.indexAlign != null && binData.indexAlign != 0)
                        writer.Write((ushort)binData.indexAlign);

                    foreach (var coord in binData.vertexBuffer)
                        writer.Write((float)coord);

                    foreach (var normal in binData.normalBuffer)
                        writer.Write((float)normal);

                    foreach (var uv in binData.uvBuffer)
                        writer.Write((float)uv);
                }
                if (_gltf.images != null)
                {
                    foreach (var image in _gltf.images)
                    {
                        image.bufferView = _gltf.bufferViews.Count;

                        var bytes = File.ReadAllBytes(image.uri);
                        var byteOffset = _gltf.bufferViews[^1].byteLength + _gltf.bufferViews[^1].byteOffset;
                        var imageView = Util.CreateBufferView(0, byteOffset, bytes.Length);

                        image.uri = null;

                        foreach (var b in bytes)
                            writer.Write(b);

                        _gltf.bufferViews.Add(imageView);
                    }
                }
            }
        }

        Buffer newbuffer = new()
        {
            byteLength = 0
        };

        if (_gltf.bufferViews.Count > 0)
            newbuffer.byteLength = _gltf.bufferViews[^1].byteOffset + _gltf.bufferViews[^1].byteLength;

        _gltf.buffers.Add(newbuffer);
        _settings.CustomExporter?.Mutate(_gltf);

        if (!_settings.ExportAsGlb)
        {
            // skip buffer
            //newbuffer.uri = ...
            //byte[] data = memoryStream.ToArray();
            var json = _gltf.ToJson();
            Data = Encoding.UTF8.GetBytes(json);
        }
        else
        {
            using MemoryStream glbStream = new();
            using (BinaryWriter writer = new(glbStream))
            {
                writer.Write(Glb.Magic);
                writer.Write(Glb.Version);
                var chunksPosition = writer.BaseStream.Position;
                writer.Write(0U);
                var jsonChunkPosition = writer.BaseStream.Position;
                writer.Write(0U);
                writer.Write(Glb.ChunkFormatJson);

                using (var streamWriter = new StreamWriter(writer.BaseStream, new UTF8Encoding(false, true), 1024, true))
                using (var jsonTextWriter = new JsonTextWriter(streamWriter))
                {
                    var json = JObject.Parse(_gltf.ToJson());
                    json.WriteTo(jsonTextWriter);
                }

                Util.Align(writer.BaseStream, 0x20);
                var jsonChunkLength = checked((uint)(writer.BaseStream.Length - jsonChunkPosition)) - Glb.ChunkHeaderLength;
                writer.BaseStream.Seek(jsonChunkPosition, SeekOrigin.Begin);
                writer.Write(jsonChunkLength);
                byte[] data = memoryStream.ToArray();
                writer.BaseStream.Seek(0, SeekOrigin.End);
                var binChunkPosition = writer.BaseStream.Position;
                writer.Write(0);
                writer.Write(Glb.ChunkFormatBin);

                writer.Write(data);

                Util.Align(writer.BaseStream, 0x20);
                var binChunkLength = checked((uint)(writer.BaseStream.Length - binChunkPosition)) - Glb.ChunkHeaderLength;
                writer.BaseStream.Seek(binChunkPosition, SeekOrigin.Begin);
                writer.Write(binChunkLength);
                var length = checked((uint)writer.BaseStream.Length);
                writer.BaseStream.Seek(chunksPosition, SeekOrigin.Begin);
                writer.Write(length);
            }

            Data = glbStream.ToArray();
        }
    }

    public bool IsCanceled() => false;

    public RenderNodeAction OnViewBegin(ViewNode node)
    {
        node.LevelOfDetail = _settings.BoxInstances ? 0 : _settings.Quality;

        if (!_settings.ExportCamera)
            return RenderNodeAction.Proceed;

        // add camera
        var cameraInfo = node.GetCameraInfo();
        var aspect = 1.0;

        Camera camera;

        if (cameraInfo.IsPerspective)
        {
            camera = new()
            {
                type = CameraType.perspective,
                perspective = new PerspectiveCamera
                {
                    aspectRatio = (float)aspect,
                    yfov = 1.0f,
                    zfar = 1000.0f,
                    znear = 0.1f
                }
            };
        }
        else
        {
            var mag = (float)(cameraInfo.VerticalExtent * 0.5);

            camera = new()
            {
                type = CameraType.orthographic,
                orthographic = new OrthographicCamera
                {
                    xmag = mag,
                    ymag = mag,
                    zfar = 1000.0f,
                    znear = 0.0f
                }
            };
        }

        var orientation = _view.GetOrientation();
        var position = orientation.EyePosition;
        var yAxis = orientation.ForwardDirection.CrossProduct(orientation.UpDirection);
        var rotation = Util.MakeQuaternion(yAxis, orientation.UpDirection);
        var targetDistance = (cameraInfo.NearDistance + cameraInfo.FarDistance) * 0.5;
        var target = position.Add(orientation.ForwardDirection * targetDistance);

        Node cameraNode = new()
        {
            name = "Revit camera",
            camera = 0,
            translation = position.ToFloats(),
            rotation = rotation,
            extras = new() { { "target", target.ToFloats() } }
        };

        _gltf.AddNode(cameraNode);
        _gltf.cameras ??= new();
        _gltf.cameras.Add(camera);

        return RenderNodeAction.Proceed;
    }

    public void OnViewEnd(ElementId elementId) { }

    public RenderNodeAction OnElementBegin(ElementId elementId)
    {
        if (_elements is not null && !_elements.Contains(elementId))
        {
            _elementData = null;
            return RenderNodeAction.Skip;
        }

        var categories = new[]
        {
            BuiltInCategory.OST_Grids,
            BuiltInCategory.OST_Levels,
            BuiltInCategory.OST_Cameras
        };

        var element = Doc.GetElement(elementId);

        if (categories.Any(c => element.Category.BuiltInCategory == c))
        {
            _elementData = null;
            return RenderNodeAction.Skip;
        }

        _elementData = new()
        {
            Element = element,
            MaterialName = null,
            SymbolId = null
        };

        return RenderNodeAction.Proceed;
    }

    public void OnElementEnd(ElementId elementId)
    {
        if (_elementData is null)
            return;

        WriteElement(_elementData, elementId);
    }

    public RenderNodeAction OnInstanceBegin(InstanceNode node)
    {
        if (_elementData is null)
            return RenderNodeAction.Skip;

        _transformStack.Push(CurrentTransform.Multiply(node.GetTransform()));
        ElementId symId = node.GetSymbolGeometryId().SymbolId;
        Element symElem = Doc.GetElement(symId);
        _elementData.SymbolId = symElem.UniqueId;

        if (_mapSymbolId.ContainsKey(symElem.UniqueId))
            return RenderNodeAction.Skip;

        return RenderNodeAction.Proceed;
    }

    public void OnInstanceEnd(InstanceNode node)
    {
        if (_elementData is null)
            return;

        ElementId symbolId = node.GetSymbolGeometryId().SymbolId;
        Element symbol = Doc.GetElement(symbolId);

        if (_mapSymbolId.TryGetValue(symbol.UniqueId, out int value))
        {
            Node gltfNode = new()
            {
                name = _elementData.Element.Name
            };

            _gltf.nodes.Add(gltfNode);
            _elementData.InstanceNodes.Add(_gltf.nodes.Count - 1);
            gltfNode.matrix = CurrentTransform.ToFloats();
            gltfNode.mesh = value;
        }
        else
        {
            WriteElementId(_elementData, symbolId, true);
        }

        _transformStack.Pop();
    }

    public RenderNodeAction OnLinkBegin(LinkNode node)
    {
        if (_elementData is null)
            return RenderNodeAction.Skip;

        _documentStack.Push(node.GetDocument());
        _transformStack.Push(CurrentTransform.Multiply(node.GetTransform()));
        return RenderNodeAction.Proceed;
    }

    public void OnLinkEnd(LinkNode node)
    {
        _documentStack.Pop();
        _transformStack.Pop();
    }

    public RenderNodeAction OnFaceBegin(FaceNode node) => RenderNodeAction.Skip;

    public void OnFaceEnd(FaceNode node) { }

    public void OnRPC(RPCNode node) { }

    public void OnLight(LightNode node) { }

    public void OnMaterial(MaterialNode node)
    {
        if (_elementData is null)
            return;

        var alpha = 1.0f - (float)node.Transparency;
        var color = node.Color;
        var id = node.MaterialId;

        bool hasMaterial = id != ElementId.InvalidElementId;
        string materialName;

        if (hasMaterial)
        {
            Element material = Doc.GetElement(id);
            materialName = material.Name;
        }
        else
        {
            var alphaByte = (byte)(alpha * 255);
            materialName = $"Basic {color.Red}-{color.Green}-{color.Blue}-{alphaByte}";
        }

        materialName = _settings.CustomExporter?.SetMaterialName(node) ?? materialName;

        if (!_mapMaterial.ContainsKey(materialName))
        {
            Pbr pbr = new()
            {
                metallicFactor = 0f,
                roughnessFactor = 1f,
                baseColorFactor = color is null ? null : new() { color.Red / 255f, color.Green / 255f, color.Blue / 255f, alpha }
            };

            Material gl_mat = new()
            {
                name = materialName,
                index = _gltf.materials.Count,
                pbrMetallicRoughness = pbr
            };

            if (alpha != 1)
            {
                gl_mat.alphaMode = "BLEND";
                gl_mat.doubleSided = true;
            }

            _gltf.materials.Add(gl_mat);
            _mapMaterial.Add(materialName, gl_mat);

            if (hasMaterial && _settings.ExportTextures)
            {
                Asset currentAsset = node.HasOverriddenAppearance ? node.GetAppearanceOverride() : node.GetAppearance();
                var assetPropertyString = Util.ReadAssetProperty(currentAsset);

                if (assetPropertyString is not null)
                {
                    string textureFile = assetPropertyString.Split('|')[0];
                    var texturePath = Path.Combine(_texturesFolder, textureFile.Replace("/", "\\"));

                    if (!File.Exists(texturePath))
                        throw new($"Texture '{texturePath}' does not exist");

                    _gltf.images ??= new();
                    _gltf.textures ??= new();

                    pbr.baseColorFactor = null;

                    BaseColorTexture bct = new()
                    {
                        index = _gltf.textures.Count
                    };

                    pbr.baseColorTexture = bct;

                    Texture texture = new()
                    {
                        source = _gltf.images.Count,
                        sampler = 0
                    };

                    _gltf.textures.Add(texture);

                    Image image = new()
                    {
                        name = Path.GetFileNameWithoutExtension(texturePath),
                        mimeType = Util.FromFileExtension(texturePath),
                        uri = texturePath
                    };

                    _gltf.images.Add(image);

                    if (_gltf.samplers is null)
                    {
                        Sampler sampler = new()
                        {
                            magFilter = 9729,
                            minFilter = 9987,
                            wrapS = 10497,
                            wrapT = 10497
                        };

                        _gltf.samplers = new() { sampler };
                    }
                }
            }
        }

        _elementData.MaterialName = materialName;
        var mapBinaryData = _elementData.MapBinaryData;

        if (!mapBinaryData.ContainsKey(materialName))
            mapBinaryData.Add(materialName, new());
    }

    public void OnPolymesh(PolymeshTopology node)
    {
        if (_elementData is null)
            return;

        if (_elementData.MaterialName is null)
            throw new("Current material is null");

        var mapBinaryData = _elementData.MapBinaryData;
        var materialName = _elementData.MaterialName;

        var currentGeometry = mapBinaryData[materialName];
        var vertexBuffer = currentGeometry.vertexBuffer;
        var uvBuffer = currentGeometry.uvBuffer;
        var indexBuffer = currentGeometry.indexBuffer;
        var index = vertexBuffer.Count / 3;

        foreach (var point in node.GetPoints())
        {
            var (x, y, z) = point.ToYUp();
            vertexBuffer.Add(x);
            vertexBuffer.Add(y);
            vertexBuffer.Add(z);
        }

        foreach (UV uv in node.GetUVs())
        {
            uvBuffer.Add((float)uv.U * 0.5f);
            uvBuffer.Add((float)uv.V * 0.5f);
        }

        //foreach (XYZ normal in node.GetNormals())
        //{
        //    currentGeometry.normalBuffer.Add((float)normal.X);
        //    currentGeometry.normalBuffer.Add((float)normal.Y);
        //    currentGeometry.normalBuffer.Add((float)normal.Z);
        //}

        foreach (PolymeshFacet facet in node.GetFacets())
        {
            var index1 = facet.V1 + index;
            var index2 = facet.V2 + index;
            var index3 = facet.V3 + index;
            indexBuffer.Add(index1);
            indexBuffer.Add(index2);
            indexBuffer.Add(index3);

            if (!currentGeometry.indexMax.HasValue)
                currentGeometry.indexMax = 0;

            if (index1 > currentGeometry.indexMax)
            {
                currentGeometry.indexMax = index1;
            }
            else if (index2 > currentGeometry.indexMax)
            {
                currentGeometry.indexMax = index2;
            }
            else if (index3 > currentGeometry.indexMax)
            {
                currentGeometry.indexMax = index3;
            }
        }
    }

    void WriteElement(ElementData element, ElementId elementId)
    {
        var instanceNodes = element.InstanceNodes;
        var mapBinaryData = element.MapBinaryData;

        if (instanceNodes.Count == 0 && mapBinaryData.Keys.Count > 0)
        {
            WriteElementId(element, elementId, false);
        }
        else if (instanceNodes.Count > 0)
        {
            var e = Doc.GetElement(elementId);
            Node node = new()
            {
                name = e.Name
            };

            _gltf.AddNode(node);

            node.children = new List<int>();
            node.children.AddRange(instanceNodes);
            node.extras = new()
            {
                { "ElementID", e.Id.IntegerValue },
                { "UniqueId", e.UniqueId }
            };
        }
    }

    void WriteElementId(ElementData element, ElementId elementId, bool isInstance)
    {
        if (element.MapBinaryData.Keys.Count > 0)
        {
            var e = Doc.GetElement(elementId);
            var meshID = _gltf.meshes.Count;
            Node node = new()
            {
                name = e.Name,
                mesh = meshID
            };

            if (element.SymbolId is not null && !CurrentTransform.IsIdentity)
            {
                if (!_mapSymbolId.ContainsKey(element.SymbolId))
                    _mapSymbolId.Add(element.SymbolId, meshID);

                node.matrix = CurrentTransform.ToFloats();
            }
            _gltf.nodes.Add(node);

            if (isInstance)
            {
                element.InstanceNodes.Add(_gltf.nodes.Count - 1);
            }
            else
            {
                node.extras = new()
                {
                    { "ElementID", e.Id.IntegerValue },
                    { "UniqueId", e.UniqueId }
                };

                _gltf.nodes[0].children?.Add(_gltf.nodes.Count - 1);
            }

            Mesh mesh = new();
            _gltf.meshes.Add(mesh);
            mesh.primitives = new();

            foreach (var key in element.MapBinaryData.Keys)
            {
                var bufferData = element.MapBinaryData[key];

                if (isInstance && _settings.BoxInstances)
                {
                    Util.CreateBox(bufferData.vertexBuffer, out var vertices, out var faces);
                    bufferData.vertexBuffer.Clear();
                    bufferData.vertexBuffer.AddRange(vertices);
                    bufferData.indexBuffer.Clear();
                    bufferData.indexBuffer.AddRange(faces);
                    bufferData.indexMax = faces.Max();
                    bufferData.uvBuffer.Clear();
                    bufferData.normalBuffer.Clear();
                }

                MeshPrimitive primitive = new()
                {
                    material = _mapMaterial[key].index
                };

                mesh.primitives.Add(primitive);

                if (bufferData.indexBuffer.Count > 0)
                {
                    Util.AddIndexsBufferViewAndAccessor(_gltf, bufferData);
                    primitive.indices = _gltf.accessors.Count - 1;
                }

                if (bufferData.vertexBuffer.Count > 0)
                {
                    _gltf.AddVec3BufferViewAndAccessor(bufferData);
                    primitive.attributes.POSITION = _gltf.accessors.Count - 1;
                }

                if (bufferData.normalBuffer.Count > 0)
                {
                    _gltf.AddNormalBufferViewAndAccessor(bufferData);
                    primitive.attributes.NORMAL = _gltf.accessors.Count - 1;
                }

                if (bufferData.uvBuffer.Count > 0)
                {
                    _gltf.AddUvBufferViewAndAccessor(bufferData);
                    primitive.attributes.TEXCOORD_0 = _gltf.accessors.Count - 1;
                }

                if (_dracoData is not null)
                {
                    var bufferViews = _dracoData.BufferViews;
                    primitive.extensions = new();
                    var dracoPrimitive = primitive.extensions.KHR_draco_mesh_compression;
                    dracoPrimitive.bufferView = bufferViews.Count;
                    dracoPrimitive.attributes.POSITION = 0;
                    dracoPrimitive.attributes.NORMAL = 1;
                    dracoPrimitive.attributes.TEXCOORD_0 = 2;
                    int byteOffset = 0;
                    int byteLength = 0;
                    var dracoBufferView = Util.CreateBufferView(0, byteOffset, byteLength);
                    bufferViews.Add(dracoBufferView);
                    _dracoData.Tasks.Add(Task.Run(() => Draco.Compression(bufferData)));
                }

                _allBinaryDatas.Add(bufferData);
            }

            element.MapBinaryData.Clear();
        }
    }
}
