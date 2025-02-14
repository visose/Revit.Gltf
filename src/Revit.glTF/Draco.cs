// Copyright 2016 The Draco Authors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//

using System.Runtime.InteropServices;

namespace RevitGltf;

class Draco
{

    [DllImport("DracoNet.dll", CallingConvention = CallingConvention.Cdecl)]
    extern static IntPtr dracoEncoder(float[] positions, float[] uvs, float[] normals, int[] indexs,
        int num_obj_faces, int num_positions, int num_tex_coords, int num_normals,
        DracoEncoderOptions options, ref int length);

    [DllImport("DracoNet.dll", CallingConvention = CallingConvention.Cdecl)]
    public extern static void deleteDracoData(IntPtr data);

    struct DracoEncoderOptions
    {
        public DracoEncoderOptions(int x)
        {
            pos_quantization_bits = 11;
            tex_coords_quantization_bits = 10;
            normals_quantization_bits = 8;
            generic_quantization_bits = 8;
            compression_level = 7;
        }

        public int pos_quantization_bits;
        public int tex_coords_quantization_bits;
        public int normals_quantization_bits;
        public int generic_quantization_bits;
        public int compression_level;
    };

    public static void Compression(BinaryData bufferData)
    {
        float[] positions = bufferData.vertexBuffer.ToArray();
        float[] uvs = bufferData.uvBuffer.ToArray();
        float[] normals = bufferData.normalBuffer.ToArray();
        int[] indexs = bufferData.indexBuffer.ToArray();

        int length = 0;
        DracoEncoderOptions options = new(0);

        int num_obj_faces = indexs.Length / 3;
        int num_positions = positions.Length / 3;
        int num_normals = normals.Length / 3;
        int num_tex_coords = uvs.Length / 2;

        var piBuf = dracoEncoder(positions, uvs, normals, indexs,
           num_obj_faces, num_positions, num_tex_coords, num_normals, options, ref length);
        bufferData.dracoData = piBuf;
        bufferData.dracoSize = length;
    }
}
