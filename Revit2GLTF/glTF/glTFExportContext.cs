using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;

namespace Revit2Gltf.glTF;

class GltfExportContext : IExportContext
{
    public byte[]? Data { get; private set; }

    readonly View3D _view;
    readonly GltfSettings _settings;

    readonly string _gltfOutDir;
    readonly GLTF _glTF;
    readonly string _texturesFolder;

    readonly Stack<Document> _documentStack = new();
    readonly Stack<Transform> _transformStack = new();
    Document Doc => _documentStack.Peek();
    Transform CurrentTransform => _transformStack.Peek();

    string? _curMaterialName;
    readonly Dictionary<string, glTFMaterial> _mapMaterial = new();
    Dictionary<string, glTFBinaryData> _curMapBinaryData = new();
    readonly List<glTFBinaryData> _allBinaryDatas;
    readonly Dictionary<string, int> _mapSymbolId = new();
    string? _curSymbolId;
    Element? _element;

    readonly List<int> _elementInstanceNodelist = new();
    readonly List<glTFBufferView>? _dracoBufferViews;

    //draco multithreading
    readonly List<Task>? _taskList;

    public GltfExportContext(View3D view, GltfSettings settings)
    {
        _view = view;
        _documentStack.Push(view.Document);
        _transformStack.Push(Transform.Identity);
        _texturesFolder = @"C:\Program Files (x86)\Common Files\Autodesk Shared\Materials\Textures\";

        _settings = settings;
        _gltfOutDir = Path.GetDirectoryName(_settings.FileName) + "\\";
        _glTF = new GLTF();
        if (_settings.UseDraco)
        {
            _glTF.extensionsRequired = new() { "KHR_draco_mesh_compression" };
            _glTF.extensionsUsed = new() { "KHR_draco_mesh_compression" };
            _dracoBufferViews = new();
            _taskList = new();
        }
        _glTF.asset = new glTFVersion();
        _glTF.scenes = new List<glTFScene>();
        _glTF.nodes = new List<glTFNode>();
        _glTF.meshes = new List<glTFMesh>();
        _glTF.bufferViews = new List<glTFBufferView>();
        _glTF.accessors = new List<glTFAccessor>();
        _glTF.buffers = new List<glTFBuffer>();
        _glTF.materials = new List<glTFMaterial>();

        glTFScene scence = new()
        {
            nodes = new() { 0 }
        };

        _glTF.scenes.Add(scence);
        const float scale = 0.3048f;

        glTFNode root = new()
        {
            name = "root",
            children = new(),
            matrix = new()
            {
                scale, 0.0, 0.0, 0.0,
                0.0, 0.0, -scale, 0.0,
                0.0, scale, 0.0, 0.0,
                0.0, 0.0, 0.0, 1.0
            }
        };

        _glTF.nodes.Add(root);
        _allBinaryDatas = new();
    }

    public bool Start()
    {
        return true;
    }

    public void Finish()
    {
        using MemoryStream memoryStream = new();
        using (BinaryWriter writer = new(memoryStream))
        {
            if (_settings.UseDraco)
            {
                // wait for thread to finish
                Task.WaitAll(_taskList?.ToArray());
                var Binarylength = _allBinaryDatas.Count;
                for (int i = 0; i < Binarylength; i++)
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
                    try
                    {
                        glTFDraco.deleteDracoData(data);
                    }
                    catch (Exception)
                    {

                        throw;
                    }
                    int byteOffset = 0;
                    if (i > 0)
                    {
                        byteOffset = _dracoBufferViews[i - 1].byteLength + _dracoBufferViews[i - 1].byteOffset;
                    }
                    _dracoBufferViews[i].byteOffset = byteOffset;
                    _dracoBufferViews[i].byteLength = size;
                }

                _glTF.bufferViews = _dracoBufferViews;

                foreach (var accessor in _glTF.accessors)
                {
                    accessor.bufferView = null;
                    accessor.byteOffset = null;
                }
                if (_glTF.images != null)
                {
                    foreach (var image in _glTF.images)
                    {
                        image.bufferView = _glTF.bufferViews.Count;

                        var bytes = File.ReadAllBytes(image.uri);
                        var byteOffset = _glTF.bufferViews[_glTF.bufferViews.Count - 1].byteLength + _glTF.bufferViews[_glTF.bufferViews.Count - 1].byteOffset;
                        var imageView = glTFUtil.addBufferView(0, byteOffset, bytes.Length);
                        image.uri = null;
                        foreach (var b in bytes)
                        {
                            writer.Write(b);
                        }
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
                    {
                        writer.Write((ushort)binData.indexAlign);
                    }
                    foreach (var coord in binData.vertexBuffer)
                    {
                        writer.Write((float)coord);
                    }
                    foreach (var normal in binData.normalBuffer)
                    {
                        writer.Write((float)normal);
                    }
                    foreach (var uv in binData.uvBuffer)
                    {
                        writer.Write((float)uv);
                    }
                }
                if (_glTF.images != null)
                {
                    foreach (var image in _glTF.images)
                    {
                        image.bufferView = _glTF.bufferViews.Count;

                        var bytes = File.ReadAllBytes(image.uri);
                        var byteOffset = _glTF.bufferViews[_glTF.bufferViews.Count - 1].byteLength + _glTF.bufferViews[_glTF.bufferViews.Count - 1].byteOffset;
                        var imageView = glTFUtil.addBufferView(0, byteOffset, bytes.Length);

                        image.uri = null;
                        foreach (var b in bytes)
                        {
                            writer.Write(b);
                        }
                        _glTF.bufferViews.Add(imageView);
                    }
                }
            }
        }

        glTFBuffer newbuffer = new()
        {
            byteLength = _glTF.bufferViews[^1].byteOffset + _glTF.bufferViews[^1].byteLength
        };

        _glTF.buffers = new() { newbuffer };
        _glTF.cameras = new();
        //AddPerspectiveCamera(_view);

        var fileExtension = Path.GetExtension(_settings.FileName).ToLower();
        if (fileExtension == ".gltf")
        {
            newbuffer.uri = Path.GetFileNameWithoutExtension(_settings.FileName) + ".bin";
            var binFileName = Path.GetFileNameWithoutExtension(_settings.FileName) + ".bin";
            using (FileStream f = File.Create(Path.Combine(_gltfOutDir, binFileName)))
            {
                byte[] data = memoryStream.ToArray();
                f.Write(data, 0, data.Length);
            }

            File.WriteAllText(_settings.FileName, _glTF.ToJson(), Encoding.UTF8);
        }
        else if (fileExtension == ".glb")
        {
            //using (var fileStream = File.Create(setting.fileName))
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
                glTFUtil.Align(writer.BaseStream, 0x20);
                var jsonChunkLength = checked((uint)(writer.BaseStream.Length - jsonChunkPosition)) - GLB.ChunkHeaderLength;
                writer.BaseStream.Seek(jsonChunkPosition, SeekOrigin.Begin);
                writer.Write(jsonChunkLength);
                byte[] data = memoryStream.ToArray();
                writer.BaseStream.Seek(0, SeekOrigin.End);
                var binChunkPosition = writer.BaseStream.Position;
                writer.Write(0);
                writer.Write(GLB.ChunkFormatBin);
                foreach (var b in data)
                {
                    writer.Write(b);
                }
                glTFUtil.Align(writer.BaseStream, 0x20);
                var binChunkLength = checked((uint)(writer.BaseStream.Length - binChunkPosition)) - GLB.ChunkHeaderLength;
                writer.BaseStream.Seek(binChunkPosition, SeekOrigin.Begin);
                writer.Write(binChunkLength);
                var length = checked((uint)writer.BaseStream.Length);
                writer.BaseStream.Seek(chunksPosition, SeekOrigin.Begin);
                writer.Write(length);
            }

            Data = glbStream.ToArray();
        }
        memoryStream.Dispose();
    }

    public bool IsCanceled()
    {
        return false;
    }

    public RenderNodeAction OnElementBegin(ElementId elementId)
    {
        _elementInstanceNodelist.Clear();
        _curSymbolId = null;
        _element = Doc.GetElement(elementId);
        _curMapBinaryData = new Dictionary<string, glTFBinaryData>();
        return RenderNodeAction.Proceed;
    }

    public void OnElementEnd(ElementId elementId)
    {
        WriteElement(elementId);
    }

    public RenderNodeAction OnFaceBegin(FaceNode node)
    {
        return RenderNodeAction.Proceed;
    }

    public void OnFaceEnd(FaceNode node)
    {

    }

    public RenderNodeAction OnInstanceBegin(InstanceNode node)
    {
        _transformStack.Push(CurrentTransform.Multiply(node.GetTransform()));
        ElementId symId = node.GetSymbolGeometryId().SymbolId;
        Element symElem = Doc.GetElement(symId);
        _curSymbolId = symElem.UniqueId;

        if (_mapSymbolId.ContainsKey(symElem.UniqueId))
        {
            return RenderNodeAction.Skip;
        }
        return RenderNodeAction.Proceed;
    }

    public void OnInstanceEnd(InstanceNode node)
    {
        ElementId symId = node.GetSymbolGeometryId().SymbolId;
        Element symElem = Doc.GetElement(symId);

        if (_mapSymbolId.TryGetValue(symElem.UniqueId, out int value))
        {
            glTFNode gltfNode = new()
            {
                name = _element.Name
            };

            _glTF.nodes.Add(gltfNode);
            _elementInstanceNodelist.Add(_glTF.nodes.Count - 1);
            Transform t = CurrentTransform;
            gltfNode.matrix = new() {
                    t.BasisX.X, t.BasisX.Y, t.BasisX.Z, 0,
                    t.BasisY.X, t.BasisY.Y, t.BasisY.Z, 0,
                    t.BasisZ.X, t.BasisZ.Y, t.BasisZ.Z, 0,
                    t.Origin.X, t.Origin.Y, t.Origin.Z, 1,
                    };
            gltfNode.mesh = value;
        }
        else
        {
            WriteElementId(symId, true);
        }

        _transformStack.Pop();
    }

    public void OnLight(LightNode node)
    {
        // var a = node;
    }

    public RenderNodeAction OnLinkBegin(LinkNode node)
    {
        _documentStack.Push(node.GetDocument());
        _transformStack.Push(CurrentTransform.Multiply(node.GetTransform()));
        return RenderNodeAction.Proceed;
    }

    public void OnLinkEnd(LinkNode node)
    {
        _documentStack.Pop();
        _transformStack.Pop();
    }

    public void OnMaterial(MaterialNode node)
    {
        ElementId id = node.MaterialId;
        double alpha = Math.Round(node.Transparency, 2);
        if (id != ElementId.InvalidElementId)
        {
            Element m = Doc.GetElement(node.MaterialId);
            _curMaterialName = m.Name;
            if (!_mapMaterial.ContainsKey(_curMaterialName))
            {
                glTFMaterial gl_mat = new()
                {
                    name = _curMaterialName
                };
                glTFPBR pbr = new();
                if (alpha != 0)
                {
                    gl_mat.alphaMode = "BLEND";
                    gl_mat.doubleSided = true;
                    alpha = 1 - alpha;
                }
                pbr.metallicFactor = 0f;
                // pbr.roughnessFactor = 1 - node.Smoothness / 100;
                pbr.roughnessFactor = 1f;
                gl_mat.pbrMetallicRoughness = pbr;
                gl_mat.index = _glTF.materials.Count;
                _glTF.materials.Add(gl_mat);
                try
                {
                    pbr.baseColorFactor = new() { node.Color.Red / 255f, node.Color.Green / 255f, node.Color.Blue / 255f, alpha / 1f };
                }
                catch
                {
                }

                Asset currentAsset = node.HasOverriddenAppearance ? node.GetAppearanceOverride() : node.GetAppearance();
                string assetPropertyString = glTFUtil.ReadAssetProperty(currentAsset);
                //if (assetPropertyString == null)
                //{
                //    var asset = glTFUtil.FindTextureAsset(currentAsset);
                //    if (asset != null)
                //    {
                //        assetPropertyString = (asset.FindByName("unifiedbitmap_Bitmap")
                //       as AssetPropertyString).Value;
                //    }
                //}
                if (assetPropertyString != null)
                {
                    string textureFile = assetPropertyString.Split('|')[0];
                    var texturePath = Path.Combine(_texturesFolder, textureFile.Replace("/", "\\"));
                    if (File.Exists(texturePath))
                    {
                        if (_glTF.textures == null)
                        {
                            _glTF.samplers = new();
                            _glTF.images = new();
                            _glTF.textures = new();
                        }
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
                            mimeType = glTFUtil.FromFileExtension(texturePath),
                            uri = texturePath
                        };
                        _glTF.images.Add(image);
                        if (_glTF.samplers.Count == 0)
                        {
                            glTFSampler sampler = new()
                            {
                                magFilter = 9729,
                                minFilter = 9987,
                                wrapS = 10497,
                                wrapT = 10497
                            };
                            _glTF.samplers.Add(sampler);
                        }
                    }
                    else
                    {
                    }
                }
                _mapMaterial.Add(_curMaterialName, gl_mat);
            }
        }
        else
        {
            _curMaterialName = string.Format("r{0}g{1}b{2}a{3}", node.Color.Red.ToString(),
               node.Color.Green.ToString(), node.Color.Blue.ToString(), alpha);

            if (!_mapMaterial.ContainsKey(_curMaterialName))
            {
                glTFMaterial gl_mat = new()
                {
                    name = _curMaterialName,
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
                _mapMaterial.Add(_curMaterialName, gl_mat);
            }
        }

        if (!_curMapBinaryData.ContainsKey(_curMaterialName))
        {
            _curMapBinaryData.Add(_curMaterialName, new glTFBinaryData());
        }
    }

    public void OnPolymesh(PolymeshTopology node)
    {
        if (_curMaterialName is null)
            throw new("Current material is null");

        var currentGeometry = _curMapBinaryData[_curMaterialName];
        var index = currentGeometry.vertexBuffer.Count / 3;
        IList<XYZ> pts = node.GetPoints();
        foreach (XYZ point in pts)
        {
            currentGeometry.vertexBuffer.Add((float)point.X);
            currentGeometry.vertexBuffer.Add((float)point.Y);
            currentGeometry.vertexBuffer.Add((float)point.Z);
        }
        IList<UV> uvs = node.GetUVs();
        foreach (UV uv in uvs)
        {
            currentGeometry.uvBuffer.Add((float)uv.U);
            currentGeometry.uvBuffer.Add((float)uv.V);
        }
        IList<XYZ> normals = node.GetNormals();
        if (normals != null && normals.Count > 0)
        {
            var normal = normals[0];
            for (int i = 0; i < node.NumberOfPoints; i++)
            {
                currentGeometry.normalBuffer.Add((float)normal.X);
                currentGeometry.normalBuffer.Add((float)normal.Y);
                currentGeometry.normalBuffer.Add((float)normal.Z);
            }
        }
        foreach (PolymeshFacet facet in node.GetFacets())
        {
            var index1 = facet.V1 + index;
            var index2 = facet.V2 + index;
            var index3 = facet.V3 + index;
            currentGeometry.indexBuffer.Add(index1);
            currentGeometry.indexBuffer.Add(index2);
            currentGeometry.indexBuffer.Add(index3);

            if (!currentGeometry.indexMax.HasValue)
            {
                currentGeometry.indexMax = 0;
            }

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

    public void OnRPC(RPCNode node)
    {
    }

    public RenderNodeAction OnViewBegin(ViewNode node)
    {
        return RenderNodeAction.Proceed;
    }

    public void OnViewEnd(ElementId elementId)
    {
    }

    void WriteElement(ElementId elementId)
    {
        if (_elementInstanceNodelist.Count == 0 && _curMapBinaryData.Keys.Count > 0)
        {
            WriteElementId(elementId, false);
        }
        else if (_elementInstanceNodelist.Count > 0)
        {
            var e = Doc.GetElement(elementId);
            glTFNode node = new()
            {
                name = e.Name
            };

            _glTF.nodes[0].children.Add(_glTF.nodes.Count);
            _glTF.nodes.Add(node);
            node.children = new List<int>();
            node.children.AddRange(_elementInstanceNodelist);
            node.extras = new()
            {
                { "ElementID", e.Id.IntegerValue },
                { "UniqueId", e.UniqueId }
            };

            if (_settings.ExportProperty)
            {
                node.extras.Add("Parameters", glTFUtil.GetParameter(e));
            }
        }
    }

    private void WriteElementId(ElementId elementId, bool isInstance)
    {
        if (_curMapBinaryData.Keys.Count > 0)
        {
            var e = Doc.GetElement(elementId);
            glTFNode node = new()
            {
                name = e.Name
            };

            var meshID = _glTF.meshes.Count;
            node.mesh = meshID;

            if (_curSymbolId != null && !CurrentTransform.IsIdentity)
            {
                if (!_mapSymbolId.ContainsKey(_curSymbolId))
                {
                    _mapSymbolId.Add(_curSymbolId, meshID);
                }
                Transform t = CurrentTransform;
                node.matrix = new()
                {
                    t.BasisX.X, t.BasisX.Y, t.BasisX.Z, 0,
                    t.BasisY.X, t.BasisY.Y, t.BasisY.Z, 0,
                    t.BasisZ.X, t.BasisZ.Y, t.BasisZ.Z, 0,
                    t.Origin.X, t.Origin.Y, t.Origin.Z, 1};
            }
            _glTF.nodes.Add(node);
            if (isInstance)
            {
                _elementInstanceNodelist.Add(_glTF.nodes.Count - 1);
            }
            else
            {
                node.extras = new()
                {
                    { "ElementID", e.Id.IntegerValue },
                    { "UniqueId", e.UniqueId }
                };

                if (_settings.ExportProperty)
                {
                    node.extras.Add("Parameters", glTFUtil.GetParameter(e));
                }
                _glTF.nodes[0].children.Add(_glTF.nodes.Count - 1);
            }
            glTFMesh mesh = new();
            _glTF.meshes.Add(mesh);
            mesh.primitives = new();
            foreach (var key in _curMapBinaryData.Keys)
            {
                var bufferData = _curMapBinaryData[key];
                glTFMeshPrimitive primative = new()
                {
                    material = _mapMaterial[key].index
                };
                mesh.primitives.Add(primative);
                if (bufferData.indexBuffer.Count > 0)
                {
                    glTFUtil.addIndexsBufferViewAndAccessor(_glTF, bufferData);
                    primative.indices = _glTF.accessors.Count - 1;
                }
                if (bufferData.vertexBuffer.Count > 0)
                {
                    glTFUtil.addVec3BufferViewAndAccessor(_glTF, bufferData);
                    primative.attributes.POSITION = _glTF.accessors.Count - 1;
                }
                if (bufferData.normalBuffer.Count > 0)
                {
                    glTFUtil.addNormalBufferViewAndAccessor(_glTF, bufferData);
                    primative.attributes.NORMAL = _glTF.accessors.Count - 1;
                }
                if (bufferData.uvBuffer.Count > 0)
                {
                    glTFUtil.addUvBufferViewAndAccessor(_glTF, bufferData);
                    primative.attributes.TEXCOORD_0 = _glTF.accessors.Count - 1;
                }

                if (_settings.UseDraco)
                {
                    primative.extensions = new glTFPrimitiveExtensions();
                    var dracoPrimative = primative.extensions.KHR_draco_mesh_compression;
                    dracoPrimative.bufferView = _dracoBufferViews.Count;
                    dracoPrimative.attributes.POSITION = 0;
                    dracoPrimative.attributes.NORMAL = 1;
                    dracoPrimative.attributes.TEXCOORD_0 = 2;
                    int byteOffset = 0;
                    int byteLength = 0;
                    var dracoBufferView = glTFUtil.addBufferView(0, byteOffset, byteLength);
                    _dracoBufferViews.Add(dracoBufferView);
                    _taskList.Add(Task.Run(() =>
                    {
                        glTFDraco.compression(bufferData);
                    }));
                }
                _allBinaryDatas.Add(bufferData);
            }
            _curMapBinaryData.Clear();
        }
    }

    void AddPerspectiveCamera(View3D view)
    {
        //add camera
        ViewOrientation3D orientation = view.GetOrientation();
        glTFCameras camera = new()
        {
            type = CameraType.perspective,
            perspective = new glTFPerspectiveCamera
            {
                aspectRatio = 1.0,
                yfov = 0.7,
                zfar = 1000,
                znear = 0.01
            }
        };

        glTFNode cameraNode = new();
        _glTF.nodes.Add(cameraNode);
        cameraNode.camera = 0;
        //camera position
        cameraNode.translation = new List<double>() {
            orientation.EyePosition.X,
            orientation.EyePosition.Y,
            orientation.EyePosition.Z };
        //camera direction
        var n = orientation.ForwardDirection.CrossProduct(orientation.UpDirection);
        cameraNode.rotation = glTFUtil.MakeQuaternion(n, orientation.UpDirection);
        cameraNode.name = "revit_camera";
        _glTF.cameras.Add(camera);
        _glTF.nodes[0].children.Add(_glTF.nodes.Count - 1);
    }
}
