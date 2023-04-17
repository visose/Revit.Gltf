using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;

namespace Revit2Gltf.glTF;

class DracoData
{
    public List<glTFBufferView> BufferViews { get; } = new();
    public List<Task> Tasks { get; } = new();
}

class ElementData
{
    public required Element Element { get; init; }
    public string? SymbolId { get; set; }
    public string? MaterialName { get; set; }
    public Dictionary<string, glTFBinaryData> MapBinaryData { get; } = new();
    public List<int> InstanceNodes { get; } = new();
}

class GltfExportContext : IExportContext
{
    public byte[]? Data { get; private set; }

    readonly View3D _view;
    readonly GltfSettings _settings;
    readonly HashSet<ElementId>? _elements;
    readonly string _texturesFolder;

    readonly Stack<Document> _documentStack = new();
    readonly Stack<Transform> _transformStack = new();

    readonly GLTF _glTF = new();
    readonly List<glTFBinaryData> _allBinaryDatas = new();
    readonly Dictionary<string, glTFMaterial> _mapMaterial = new();
    readonly Dictionary<string, int> _mapSymbolId = new();

    readonly DracoData? _dracoData;
    ElementData? _elementData;

    Document Doc => _documentStack.Peek();
    Transform CurrentTransform => _transformStack.Peek();

    public GltfExportContext(View3D view, GltfSettings settings)
    {
        _view = view;
        _settings = settings;
        _elements = _settings.Elements?.ToHashSet();
        _texturesFolder = @"C:\Program Files (x86)\Common Files\Autodesk Shared\Materials\Textures\";

        _documentStack.Push(view.Document);
        _transformStack.Push(Transform.Identity);

        if (_settings.UseDraco)
        {
            _glTF.extensionsRequired = new() { "KHR_draco_mesh_compression" };
            _glTF.extensionsUsed = new() { "KHR_draco_mesh_compression" };
            _dracoData = new();
        }

        glTFScene scene = new()
        {
            nodes = new() { 0 }
        };

        _glTF.scenes.Add(scene);

        glTFNode root = new()
        {
            name = "root",
            children = new()
        };

        _glTF.nodes.Add(root);
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
                    GltfDraco.deleteDracoData(data);

                    int byteOffset = 0;
                    var bufferViews = _dracoData.BufferViews;

                    if (i > 0)
                        byteOffset = bufferViews[i - 1].byteLength + bufferViews[i - 1].byteOffset;

                    bufferViews[i].byteOffset = byteOffset;
                    bufferViews[i].byteLength = size;
                }

                _glTF.bufferViews.Clear();
                _glTF.bufferViews.AddRange(_dracoData.BufferViews);

                foreach (var accessor in _glTF.accessors)
                {
                    accessor.bufferView = null;
                    accessor.byteOffset = null;
                }

                if (_glTF.images is not null)
                {
                    foreach (var image in _glTF.images)
                    {
                        image.bufferView = _glTF.bufferViews.Count;
                        var bytes = File.ReadAllBytes(image.uri);
                        var byteOffset = _glTF.bufferViews[^1].byteLength + _glTF.bufferViews[^1].byteOffset;
                        var imageView = GltfUtil.CreateBufferView(0, byteOffset, bytes.Length);
                        image.uri = null;
                        writer.Write(bytes);
                        _glTF.bufferViews.Add(imageView);
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
                if (_glTF.images != null)
                {
                    foreach (var image in _glTF.images)
                    {
                        image.bufferView = _glTF.bufferViews.Count;

                        var bytes = File.ReadAllBytes(image.uri);
                        var byteOffset = _glTF.bufferViews[^1].byteLength + _glTF.bufferViews[^1].byteOffset;
                        var imageView = GltfUtil.CreateBufferView(0, byteOffset, bytes.Length);

                        image.uri = null;

                        foreach (var b in bytes)
                            writer.Write(b);

                        _glTF.bufferViews.Add(imageView);
                    }
                }
            }
        }

        glTFBuffer newbuffer = new()
        {
            byteLength = 0
        };

        if (_glTF.bufferViews.Count > 0)
            newbuffer.byteLength = _glTF.bufferViews[^1].byteOffset + _glTF.bufferViews[^1].byteLength;

        _glTF.buffers.Add(newbuffer);
        //AddPerspectiveCamera(_view);

        if (!_settings.ExportAsGLB)
        {
            // skip buffer
            //newbuffer.uri = ...
            //byte[] data = memoryStream.ToArray();
            var json = _glTF.ToJson();
            Data = Encoding.UTF8.GetBytes(json);
        }
        else
        {
            using MemoryStream glbStream = new();
            using (BinaryWriter writer = new(glbStream))
            {
                writer.Write(GLB.Magic);
                writer.Write(GLB.Version);
                var chunksPosition = writer.BaseStream.Position;
                writer.Write(0U);
                var jsonChunkPosition = writer.BaseStream.Position;
                writer.Write(0U);
                writer.Write(GLB.ChunkFormatJson);

                using (var streamWriter = new StreamWriter(writer.BaseStream, new UTF8Encoding(false, true), 1024, true))
                using (var jsonTextWriter = new JsonTextWriter(streamWriter))
                {
                    var json = JObject.Parse(_glTF.ToJson());
                    json.WriteTo(jsonTextWriter);
                }

                GltfUtil.Align(writer.BaseStream, 0x20);
                var jsonChunkLength = checked((uint)(writer.BaseStream.Length - jsonChunkPosition)) - GLB.ChunkHeaderLength;
                writer.BaseStream.Seek(jsonChunkPosition, SeekOrigin.Begin);
                writer.Write(jsonChunkLength);
                byte[] data = memoryStream.ToArray();
                writer.BaseStream.Seek(0, SeekOrigin.End);
                var binChunkPosition = writer.BaseStream.Position;
                writer.Write(0);
                writer.Write(GLB.ChunkFormatBin);

                writer.Write(data);

                GltfUtil.Align(writer.BaseStream, 0x20);
                var binChunkLength = checked((uint)(writer.BaseStream.Length - binChunkPosition)) - GLB.ChunkHeaderLength;
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

        // add camera
        var cameraInfo = node.GetCameraInfo();
        var aspect = 1.0;

        glTFCameras camera;

        if (cameraInfo.IsPerspective)
        {
            camera = new()
            {
                type = CameraType.perspective,
                perspective = new glTFPerspectiveCamera
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
                orthographic = new glTFOrthographicCamera
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
        var rotation = GltfUtil.MakeQuaternion(yAxis, orientation.UpDirection);
        var targetDistance = (cameraInfo.NearDistance + cameraInfo.FarDistance) * 0.5;
        var target = position.Add(orientation.ForwardDirection * targetDistance);

        glTFNode cameraNode = new()
        {
            name = "Revit camera",
            camera = 0,
            translation = position.ToFloats(),
            rotation = rotation,
            extras = new() { { "target", target.ToFloats() } }
        };

        _glTF.AddNode(cameraNode);
        _glTF.cameras ??= new();
        _glTF.cameras.Add(camera);

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

        _elementData = new()
        {
            Element = Doc.GetElement(elementId),
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
            glTFNode gltfNode = new()
            {
                name = _elementData.Element.Name
            };

            _glTF.nodes.Add(gltfNode);
            _elementData.InstanceNodes.Add(_glTF.nodes.Count - 1);
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

        float alpha = 1 - (float)Math.Round(node.Transparency, 2);
        var color = node.Color;
        ElementId id = node.MaterialId;
        string materialName;

        if (id != ElementId.InvalidElementId)
        {
            Element material = Doc.GetElement(id);
            materialName = material.Name;

            if (!_mapMaterial.ContainsKey(material.Name))
            {
                glTFPBR pbr = new()
                {
                    metallicFactor = 0f,
                    //roughnessFactor = 1 - node.Smoothness / 100;
                    roughnessFactor = 1f,
                    baseColorFactor = color is null ? null : new() { color.Red / 255f, color.Green / 255f, color.Blue / 255f, alpha }
                };

                glTFMaterial gl_mat = new()
                {
                    name = materialName,
                    index = _glTF.materials.Count,
                    pbrMetallicRoughness = pbr
                };

                _glTF.materials.Add(gl_mat);
                _mapMaterial.Add(materialName, gl_mat);

                if (alpha != 1)
                {
                    gl_mat.alphaMode = "BLEND";
                    gl_mat.doubleSided = true;
                }

                if (_settings.ExportTextures)
                {
                    Asset currentAsset = node.HasOverriddenAppearance ? node.GetAppearanceOverride() : node.GetAppearance();
                    var assetPropertyString = GltfUtil.ReadAssetProperty(currentAsset);

                    if (assetPropertyString is not null)
                    {
                        string textureFile = assetPropertyString.Split('|')[0];
                        var texturePath = Path.Combine(_texturesFolder, textureFile.Replace("/", "\\"));

                        if (!File.Exists(texturePath))
                            throw new($"Texture '{texturePath}' does not exist");

                        _glTF.images ??= new();
                        _glTF.textures ??= new();

                        pbr.baseColorFactor = null;

                        glTFbaseColorTexture bct = new()
                        {
                            index = _glTF.textures.Count
                        };

                        pbr.baseColorTexture = bct;

                        glTFTexture texture = new()
                        {
                            source = _glTF.images.Count,
                            sampler = 0
                        };

                        _glTF.textures.Add(texture);

                        glTFImage image = new()
                        {
                            name = Path.GetFileNameWithoutExtension(texturePath),
                            mimeType = GltfUtil.FromFileExtension(texturePath),
                            uri = texturePath
                        };

                        _glTF.images.Add(image);

                        if (_glTF.samplers is null)
                        {
                            glTFSampler sampler = new()
                            {
                                magFilter = 9729,
                                minFilter = 9987,
                                wrapS = 10497,
                                wrapT = 10497
                            };

                            _glTF.samplers = new() { sampler };
                        }
                    }
                }
            }
        }
        else
        {
            materialName = string.Format("r{0}g{1}b{2}a{3}", color.Red.ToString(), color.Green.ToString(), color.Blue.ToString(), alpha);

            if (!_mapMaterial.ContainsKey(materialName))
            {
                glTFMaterial gl_mat = new()
                {
                    name = materialName,
                    index = _glTF.materials.Count
                };

                if (alpha != 0)
                {
                    gl_mat.alphaMode = "BLEND";
                    gl_mat.doubleSided = true;
                    alpha = 1 - alpha;
                }

                glTFPBR pbr = new()
                {
                    baseColorFactor = new() { node.Color.Red / 255f, node.Color.Green / 255f, node.Color.Blue / 255f, alpha },
                    metallicFactor = 0f,
                    roughnessFactor = 1f
                };

                gl_mat.pbrMetallicRoughness = pbr;
                _glTF.materials.Add(gl_mat);
                _mapMaterial.Add(materialName, gl_mat);
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
            glTFNode node = new()
            {
                name = e.Name
            };

            _glTF.AddNode(node);

            node.children = new List<int>();
            node.children.AddRange(instanceNodes);
            node.extras = new()
            {
                { "ElementID", e.Id.IntegerValue },
                { "UniqueId", e.UniqueId }
            };

            if (_settings.ExportParameters)
                node.extras.Add("Parameters", GltfUtil.GetParameter(e));
        }
    }

    void WriteElementId(ElementData element, ElementId elementId, bool isInstance)
    {
        if (element.MapBinaryData.Keys.Count > 0)
        {
            var e = Doc.GetElement(elementId);
            var meshID = _glTF.meshes.Count;
            glTFNode node = new()
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
            _glTF.nodes.Add(node);

            if (isInstance)
            {
                element.InstanceNodes.Add(_glTF.nodes.Count - 1);
            }
            else
            {
                node.extras = new()
                {
                    { "ElementID", e.Id.IntegerValue },
                    { "UniqueId", e.UniqueId }
                };

                if (_settings.ExportParameters)
                    node.extras.Add("Parameters", GltfUtil.GetParameter(e));

                _glTF.nodes[0].children?.Add(_glTF.nodes.Count - 1);
            }

            glTFMesh mesh = new();
            _glTF.meshes.Add(mesh);
            mesh.primitives = new();

            foreach (var key in element.MapBinaryData.Keys)
            {
                var bufferData = element.MapBinaryData[key];

                if (isInstance && _settings.BoxInstances)
                {
                    GltfUtil.CreateBox(bufferData.vertexBuffer, out var vertices, out var faces);
                    bufferData.vertexBuffer.Clear();
                    bufferData.vertexBuffer.AddRange(vertices);
                    bufferData.indexBuffer.Clear();
                    bufferData.indexBuffer.AddRange(faces);
                    bufferData.indexMax = faces.Max();
                    bufferData.uvBuffer.Clear();
                    bufferData.normalBuffer.Clear();
                }

                glTFMeshPrimitive primitive = new()
                {
                    material = _mapMaterial[key].index
                };

                mesh.primitives.Add(primitive);

                if (bufferData.indexBuffer.Count > 0)
                {
                    GltfUtil.AddIndexsBufferViewAndAccessor(_glTF, bufferData);
                    primitive.indices = _glTF.accessors.Count - 1;
                }

                if (bufferData.vertexBuffer.Count > 0)
                {
                    _glTF.AddVec3BufferViewAndAccessor(bufferData);
                    primitive.attributes.POSITION = _glTF.accessors.Count - 1;
                }

                if (bufferData.normalBuffer.Count > 0)
                {
                    _glTF.AddNormalBufferViewAndAccessor(bufferData);
                    primitive.attributes.NORMAL = _glTF.accessors.Count - 1;
                }

                if (bufferData.uvBuffer.Count > 0)
                {
                    _glTF.AddUvBufferViewAndAccessor(bufferData);
                    primitive.attributes.TEXCOORD_0 = _glTF.accessors.Count - 1;
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
                    var dracoBufferView = GltfUtil.CreateBufferView(0, byteOffset, byteLength);
                    bufferViews.Add(dracoBufferView);
                    _dracoData.Tasks.Add(Task.Run(() => GltfDraco.Compression(bufferData)));
                }

                _allBinaryDatas.Add(bufferData);
            }

            element.MapBinaryData.Clear();
        }
    }
}
