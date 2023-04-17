using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;

namespace RevitGltf;

static class Util
{
    const float _scale = 0.3048f;

    public static void CreateBox(IList<float> points, out float[] vertices, out int[] faces)
    {
        var min = (x: double.MaxValue, y: double.MaxValue, z: double.MaxValue);
        var max = (x: double.MinValue, y: double.MinValue, z: double.MinValue);

        for (int i = 0; i < points.Count; i += 3)
        {
            float x = points[i + 0];
            float y = points[i + 1];
            float z = points[i + 2];
            min.x = Math.Min(min.x, x);
            min.y = Math.Min(min.y, y);
            min.z = Math.Min(min.z, z);
            max.x = Math.Max(max.x, x);
            max.y = Math.Max(max.y, y);
            max.z = Math.Max(max.z, z);
        }

        BoundingBoxXYZ box = new()
        {
            Min = new(min.x, min.y, min.z),
            Max = new(max.x, max.y, max.z)
        };

        CreateBox(box, out vertices, out faces);
    }

    static void CreateBox(BoundingBoxXYZ box, out float[] vertices, out int[] faces)
    {
        var (x0, y0, z0) = box.Min.Deconstruct();
        var (x1, y1, z1) = box.Max.Deconstruct();

        vertices = new float[8 * 3]
        {
            x0, y0, z1,
            x1, y0, z1,
            x1, y1, z1,
            x0, y1, z1,
            x0, y0, z0,
            x1, y0, z0,
            x1, y1, z0,
            x0, y1, z0,
        };

        faces = new int[12 * 3]
        {
            0, 1, 2,
            0, 2, 3,
            4, 0, 3,
            4, 3, 7,
            1, 5, 6,
            1, 6, 2,
            4, 7, 6,
            4, 6, 5,
            3, 2, 6,
            3, 6, 7,
            4, 5, 1,
            4, 1, 0,
        };
    }

    public static (float X, float Y, float Z) ToYUp(this XYZ p)
    {
        return ((float)p.X * _scale, (float)p.Z * _scale, (float)-p.Y * _scale);
    }

    public static (float X, float Y, float Z) Deconstruct(this XYZ p)
    {
        return ((float)p.X, (float)p.Y, (float)p.Z);
    }

    public static float[] ToFloats(this XYZ p)
    {
        var (x, y, z) = p.ToYUp();
        return new float[3] { x, y, z };
    }

    public static float[] ToFloats(this Transform t)
    {
        var (x, y, z) = t.Origin.ToYUp();

        return new float[16]
        {
            (float)t.BasisX.X, (float)t.BasisX.Z, (float)-t.BasisX.Y, 0,
            (float)t.BasisZ.X, (float)t.BasisZ.Z, (float)-t.BasisZ.Y, 0,
            (float)-t.BasisY.X, (float)-t.BasisY.Z, (float)t.BasisY.Y, 0,
            x, y, z, 1,
        };
    }

    public static void AddNode(this Gltf gltf, Node node)
    {
        gltf.nodes.Add(node);
        int count = gltf.nodes.Count;

        if (count > 1)
            gltf.nodes[0].children?.Add(gltf.nodes.Count - 1);
    }

    public static void AddVec3BufferViewAndAccessor(this Gltf gltf, BinaryData bufferData)
    {
        var v3ds = bufferData.vertexBuffer;
        var byteOffset = 0;

        if (gltf.bufferViews.Count > 0)
            byteOffset = gltf.bufferViews[^1].byteLength + gltf.bufferViews[^1].byteOffset;

        var bufferIndex = 0;
        var vec3View = CreateBufferView(bufferIndex, byteOffset, 4 * v3ds.Count);
        vec3View.target = Targets.ARRAY_BUFFER;
        gltf.bufferViews.Add(vec3View);
        var vecAccessor = CreateAccessor(gltf.bufferViews.Count - 1, 0, ComponentType.FLOAT, v3ds.Count / 3, AccessorType.VEC3);
        GetBounds(v3ds, 3, out var min, out var max);
        vecAccessor.min = min;
        vecAccessor.max = max;
        gltf.accessors.Add(vecAccessor);
    }

    public static void AddNormalBufferViewAndAccessor(this Gltf gltf, BinaryData bufferData)
    {
        var v3ds = bufferData.normalBuffer;
        var byteOffset = 0;

        if (gltf.bufferViews.Count > 0)
            byteOffset = gltf.bufferViews[^1].byteLength + gltf.bufferViews[^1].byteOffset;

        var bufferIndex = 0;
        var vec3View = CreateBufferView(bufferIndex, byteOffset, 4 * v3ds.Count);
        vec3View.target = Targets.ARRAY_BUFFER;
        gltf.bufferViews.Add(vec3View);
        var vecAccessor = CreateAccessor(gltf.bufferViews.Count - 1, 0, ComponentType.FLOAT, v3ds.Count / 3, AccessorType.VEC3);
        gltf.accessors.Add(vecAccessor);
    }

    public static void AddIndexsBufferViewAndAccessor(this Gltf gltf, BinaryData bufferData)
    {
        var byteOffset = 0;

        if (gltf.bufferViews.Count > 0)
            byteOffset = gltf.bufferViews[^1].byteLength + gltf.bufferViews[^1].byteOffset;

        var bufferIndex = 0;
        BufferView faceView;
        Accessor faceAccessor;
        var length = bufferData.indexBuffer.Count;

        if (bufferData.indexMax > 65535)
        {
            faceView = CreateBufferView(bufferIndex, byteOffset, 4 * length);
            faceView.target = Targets.ELEMENT_ARRAY_BUFFER;
            gltf.bufferViews.Add(faceView);
            faceAccessor = CreateAccessor(gltf.bufferViews.Count - 1, 0, ComponentType.UNSIGNED_INT, length, AccessorType.SCALAR);
        }
        else
        {
            var align = 0;

            if ((2 * length) % 4 != 0)
            {
                align = 2;
                bufferData.indexAlign = 0x20;
            }

            faceView = CreateBufferView(bufferIndex, byteOffset, 2 * length + align);
            faceView.target = Targets.ELEMENT_ARRAY_BUFFER;
            gltf.bufferViews.Add(faceView);
            faceAccessor = CreateAccessor(gltf.bufferViews.Count - 1, 0, ComponentType.UNSIGNED_SHORT, length, AccessorType.SCALAR);
        }
        gltf.accessors.Add(faceAccessor);
    }

    public static void AddUvBufferViewAndAccessor(this Gltf gltf, BinaryData bufferData)
    {
        var uvs = bufferData.uvBuffer;
        var byteOffset = 0;

        if (gltf.bufferViews.Count > 0)
            byteOffset = gltf.bufferViews[^1].byteLength + gltf.bufferViews[^1].byteOffset;

        var bufferIndex = 0;
        var vec2View = CreateBufferView(bufferIndex, byteOffset, 4 * uvs.Count);
        vec2View.target = Targets.ARRAY_BUFFER;
        gltf.bufferViews.Add(vec2View);
        var vecAccessor = CreateAccessor(gltf.bufferViews.Count - 1, 0, ComponentType.FLOAT, uvs.Count / 2, AccessorType.VEC2);
        gltf.accessors.Add(vecAccessor);
    }

    public static BufferView CreateBufferView(int bufferIndex, int byteOffset, int byteLength)
    {
        return new()
        {
            buffer = bufferIndex,
            byteOffset = byteOffset,
            byteLength = byteLength
        };
    }

    static Accessor CreateAccessor(int bufferView, int byteOffset, ComponentType componentType, int count, string type)
    {
        return new()
        {
            bufferView = bufferView,
            byteOffset = byteOffset,
            componentType = componentType,
            count = count,
            type = type
        };
    }

    static void GetBounds(IList<float> floats, int dimensions, out float[] min, out float[] max)
    {
        min = new float[dimensions];
        max = new float[dimensions];

        for (int i = 0; i < dimensions; i++)
        {
            min[i] = float.MaxValue;
            max[i] = float.MinValue;
        }

        for (int i = 0; i < floats.Count; i += dimensions)
        {
            for (int j = 0; j < dimensions; j++)
            {
                float value = floats[i + j];
                min[j] = Math.Min(min[j], value);
                max[j] = Math.Max(max[j], value);
            }
        }
    }

    public static void Align(Stream stream, byte pad = 0)
    {
        var count = 3 - ((stream.Position - 1) & 3);

        while (count != 0)
        {
            stream.WriteByte(pad);
            count--;
        }
    }

    public static List<ParameterGroup> GetParameter(Element element)
    {
        var parameterGroupMap = new Dictionary<string, ParameterGroup>();
        IList<Autodesk.Revit.DB.Parameter> parameters = element.GetOrderedParameters();
        foreach (Autodesk.Revit.DB.Parameter p in parameters)
        {
            string GroupName = LabelUtils.GetLabelFor(p.Definition.ParameterGroup);
            Parameter parameter = new()
            {
                name = p.Definition.Name
            };
            if (StorageType.String == p.StorageType)
            {
                parameter.value = p.AsString();
            }
            else
            {
                parameter.value = p.AsValueString();
            }

            if (parameterGroupMap.TryGetValue(GroupName, out ParameterGroup propertySet))
            {
                propertySet.Parameters.Add(parameter);
            }
            else
            {
                propertySet = new()
                {
                    GroupName = GroupName
                };

                propertySet.Parameters.Add(parameter);
                parameterGroupMap.Add(GroupName, propertySet);
            }
        }

        return parameterGroupMap.Values.ToList(); ;
    }

    public static string FromFileExtension(string fileExtension)
    {
        return Path.GetExtension(fileExtension).ToLower() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".dds" => "image/vnd-ms.dds",
            _ => "image/png",
        };
    }

    public static string? ReadAssetProperty(AssetProperty prop)
    {
        switch (prop)
        {
            case AssetPropertyString propString:
                if (propString.Name == "unifiedbitmap_Bitmap")
                    return propString.Value;

                break;

            // The APT_List contains a list of sub asset properties with same type.
            case AssetPropertyList propList:
                IList<AssetProperty> subProps = propList.GetValue();

                if (subProps.Count == 0)
                    break;

                switch (subProps[0].Type)
                {
                    case AssetPropertyType.Integer:
                        if (prop is AssetPropertyString propString2 && propString2.Name == "unifiedbitmap_Bitmap")
                            return propString2.Value;

                        break;
                }

                break;

            case Asset propAsset:
                var value = ReadAsset(propAsset);

                if (!string.IsNullOrEmpty(value))
                    return value;

                break;
        }

        if (prop.NumberOfConnectedProperties == 0)
            return null;

        if (prop.Name == "generic_diffuse")
        {
            foreach (AssetProperty connectedProp in prop.GetAllConnectedProperties())
            {
                var value = ReadAssetProperty(connectedProp);

                if (!string.IsNullOrEmpty(value))
                    return value;
            }
        }

        return null;
    }

    static string? ReadAsset(Asset asset)
    {
        for (int idx = 0; idx < asset.Size; idx++)
        {
            AssetProperty prop = asset[idx];
            var value = ReadAssetProperty(prop);

            if (!string.IsNullOrEmpty(value))
                return value;
        }

        return null;
    }

    public static float[] MakeQuaternion(XYZ u, XYZ v)
    {
        var n = u.CrossProduct(v);
        double s, tr = 1 + u.X + v.Y + n.Z;
        double X, Y, Z, W;
        if (tr > 1e-4)
        {
            s = 2 * Math.Sqrt(tr);
            W = s / 4;
            X = (v.Z - n.Y) / s;
            Y = (n.X - u.Z) / s;
            Z = (u.Y - v.X) / s;
        }
        else
        {
            if (u.X > v.Y && u.X > n.Z)
            {
                s = 2 * Math.Sqrt(1 + u.X - v.Y - n.Z);
                W = (v.Z - n.Y) / s;
                X = s / 4;
                Y = (u.Y + v.X) / s;
                Z = (n.X + u.Z) / s;
            }
            else if (v.Y > n.Z)
            {
                s = 2 * Math.Sqrt(1 - u.X + v.Y - n.Z);
                W = (n.X - u.Z) / s;
                X = (u.Y + v.X) / s;
                Y = s / 4;
                Z = (v.Z + n.Y) / s;
            }
            else
            {
                s = 2 * Math.Sqrt(1 - u.X - v.Y + n.Z);
                W = (u.Y - v.X) / s;
                X = (n.X + u.Z) / s;
                Y = (v.Z + n.Y) / s;
                Z = s / 4;
            }
        }

        var magnitude = Math.Sqrt(W * W + X * X + Y * Y + Z * Z);
        var scale = 1 / magnitude;
        W *= scale;
        X *= scale;
        Y *= scale;
        Z *= scale;
        return new float[] { (float)X, (float)Y, (float)Z, (float)W };
    }

    //Use recursion to find the texture information
    static Asset? FindTextureAsset(AssetProperty ap)
    {
        Asset? result = null;

        if (ap.Type == AssetPropertyType.Asset)
        {
            var asset = (Asset)ap;

            if (!IsTextureAsset(asset))
            {
                for (int i = 0; i < asset.Size; i++)
                {
                    var textureAsset = FindTextureAsset(asset[i]);

                    if (textureAsset is not null)
                    {
                        result = textureAsset;
                        break;
                    }
                }
            }
            else
            {
                result = asset;
            }

            return result;
        }
        else
        {
            for (int j = 0; j < ap.NumberOfConnectedProperties; j++)
            {
                var textureAsset = FindTextureAsset(ap.GetConnectedProperty(j));
                if (textureAsset is not null)
                {
                    result = textureAsset;
                }
            }

            return result;
        }
    }

    //Determine whether the Asset contains texture information
    static bool IsTextureAsset(Asset asset)
    {
        var assetProprty = GetAssetProprty(asset, "assettype") as AssetPropertyString;

        if (assetProprty?.Value == "texture")
            return true;

        return GetAssetProprty(asset, "unifiedbitmap_Bitmap") != null;
    }

    //Get the corresponding AssetProperty according to the name
    static AssetProperty? GetAssetProprty(Asset asset, string propertyName)
    {
        for (int i = 0; i < asset.Size; i++)
        {
            if (asset[i].Name == propertyName)
                return asset[i];
        }

        return null;
    }
}
