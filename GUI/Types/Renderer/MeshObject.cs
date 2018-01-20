﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArcManagedFBX;
using ArcManagedFBX.IO;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.KeyValues;
using ValveResourceFormat.ResourceTypes;
using Vector3 = OpenTK.Vector3;
using Vector4 = OpenTK.Vector4;

namespace GUI.Types.Renderer
{
    internal class MeshObject
    {
        public Resource Resource { get; set; }
        public Matrix4 Transform { get; set; } = Matrix4.Identity;
        public Vector4 TintColor { get; set; } = Vector4.One;
        public List<DrawCall> DrawCalls { get; set; } = new List<DrawCall>();
        public List<string> SkinMaterials { get; set; } = new List<string>();

        /* Construct a mesh object from it's resource */
        public void LoadFromResource(MaterialLoader materialLoader)
        {
            if (Resource != null)
            {
                var block = Resource.VBIB;
                var data = (BinaryKV3)Resource.Blocks[BlockType.DATA];
                var modelArguments = (ArgumentDependencies)((ResourceEditInfo)Resource.Blocks[BlockType.REDI]).Structs[ResourceEditInfo.REDIStruct.ArgumentDependencies];

                var vertexBuffers = new uint[block.VertexBuffers.Count];
                var indexBuffers = new uint[block.IndexBuffers.Count];

                GL.GenBuffers(block.VertexBuffers.Count, vertexBuffers);
                GL.GenBuffers(block.IndexBuffers.Count, indexBuffers);

                for (var i = 0; i < block.VertexBuffers.Count; i++)
                {
                    GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffers[i]);
                    GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(block.VertexBuffers[i].Count * block.VertexBuffers[i].Size), block.VertexBuffers[i].Buffer, BufferUsageHint.StaticDraw);

                    GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out int _);
                }

                for (var i = 0; i < block.IndexBuffers.Count; i++)
                {
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, indexBuffers[i]);
                    GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(block.IndexBuffers[i].Count * block.IndexBuffers[i].Size), block.IndexBuffers[i].Buffer, BufferUsageHint.StaticDraw);

                    GL.GetBufferParameter(BufferTarget.ElementArrayBuffer, BufferParameterName.BufferSize, out int _);
                }

                //Prepare drawcalls
                var a = (KVObject)data.Data.Properties["m_sceneObjects"].Value;

                for (var b = 0; b < a.Properties.Count; b++)
                {
                    var c = (KVObject)((KVObject)a.Properties[b.ToString()].Value).Properties["m_drawCalls"].Value;

                    for (var i = 0; i < c.Properties.Count; i++)
                    {
                        var d = (KVObject)c.Properties[i.ToString()].Value;

                        var materialName = d.Properties["m_material"].Value.ToString();

                        if (SkinMaterials.Any())
                        {
                            materialName = SkinMaterials[i];
                        }

                        var material = materialLoader.GetMaterial(materialName);

                        // TODO: Don't pass around so much shit
                        var drawCall = CreateDrawCall(d.Properties, vertexBuffers, indexBuffers, modelArguments, Resource.VBIB, material);
                        DrawCalls.Add(drawCall);
                    }
                }

                DrawCalls = DrawCalls.OrderBy(x => x.Material.Name).ToList();

                // No longer need the resource, we extracted all data
                Resource = null;
            }
        }

        //Set up a draw call
        private DrawCall CreateDrawCall(Dictionary<string, KVValue> drawProperties, uint[] vertexBuffers, uint[] indexBuffers, ArgumentDependencies modelArguments, VBIB block, Material material)
        {
            var drawCall = new DrawCall();

            switch (drawProperties["m_nPrimitiveType"].Value.ToString())
            {
                case "RENDER_PRIM_TRIANGLES":
                    drawCall.PrimitiveType = PrimitiveType.Triangles;
                    break;
                default:
                    throw new Exception("Unknown PrimitiveType in drawCall! (" + drawProperties["m_nPrimitiveType"].Value + ")");
            }

            drawCall.Material = material;

            // Load shader
            drawCall.Shader = ShaderLoader.LoadShader(drawCall.Material.ShaderName, modelArguments);

            //Bind and validate shader
            GL.UseProgram(drawCall.Shader.Program);

            var f = (KVObject)drawProperties["m_indexBuffer"].Value;

            var indexBuffer = default(DrawBuffer);
            indexBuffer.Id = Convert.ToUInt32(f.Properties["m_hBuffer"].Value);
            indexBuffer.Offset = Convert.ToUInt32(f.Properties["m_nBindOffsetBytes"].Value);
            drawCall.IndexBuffer = indexBuffer;

            var bufferSize = block.IndexBuffers[(int)drawCall.IndexBuffer.Id].Size;
            drawCall.BaseVertex = Convert.ToUInt32(drawProperties["m_nBaseVertex"].Value);
            drawCall.VertexCount = Convert.ToUInt32(drawProperties["m_nVertexCount"].Value);
            drawCall.StartIndex = Convert.ToUInt32(drawProperties["m_nStartIndex"].Value) * bufferSize;
            drawCall.IndexCount = Convert.ToInt32(drawProperties["m_nIndexCount"].Value);

            if (drawProperties.ContainsKey("m_vTintColor"))
            {
                var tint = (KVObject)drawProperties["m_vTintColor"].Value;
                drawCall.TintColor = new Vector3(
                    Convert.ToSingle(tint.Properties["0"].Value),
                    Convert.ToSingle(tint.Properties["1"].Value),
                    Convert.ToSingle(tint.Properties["2"].Value));
            }

            if (bufferSize == 2)
            {
                //shopkeeper_vr
                drawCall.IndiceType = DrawElementsType.UnsignedShort;
            }
            else if (bufferSize == 4)
            {
                //glados
                drawCall.IndiceType = DrawElementsType.UnsignedInt;
            }
            else
            {
                throw new Exception("Unsupported indice type");
            }

            var g = (KVObject)drawProperties["m_vertexBuffers"].Value;
            var h = (KVObject)g.Properties["0"].Value; // TODO: Not just 0

            var vertexBuffer = default(DrawBuffer);
            vertexBuffer.Id = Convert.ToUInt32(h.Properties["m_hBuffer"].Value);
            vertexBuffer.Offset = Convert.ToUInt32(h.Properties["m_nBindOffsetBytes"].Value);
            drawCall.VertexBuffer = vertexBuffer;

            GL.GenVertexArrays(1, out uint vertexArrayObject);
            drawCall.VertexArrayObject = vertexArrayObject;

            GL.BindVertexArray(drawCall.VertexArrayObject);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffers[drawCall.VertexBuffer.Id]);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, indexBuffers[drawCall.IndexBuffer.Id]);

            var curVertexBuffer = block.VertexBuffers[(int)drawCall.VertexBuffer.Id];
            var texCoordNum = 0;
            foreach (var attribute in curVertexBuffer.Attributes)
            {
                var attributeName = "v" + attribute.Name;

                // TODO: other params too?
                if (attribute.Name == "TEXCOORD" && texCoordNum++ > 0)
                {
                    attributeName += texCoordNum;
                }

                BindVertexAttrib(attribute, attributeName, drawCall.Shader.Program, (int)curVertexBuffer.Size);
            }

            GL.BindVertexArray(0);

            return drawCall;
        }

        private void BindVertexAttrib(VBIB.VertexAttribute attribute, string attributeName, int shaderProgram, int stride)
        {
            var attributeLocation = GL.GetAttribLocation(shaderProgram, attributeName);

            //Ignore this attribute if it is not found in the shader
            if (attributeLocation == -1)
            {
                return;
            }

            GL.EnableVertexAttribArray(attributeLocation);

            switch (attribute.Type)
            {
                case DXGI_FORMAT.R32G32B32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 3, VertexAttribPointerType.Float, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R8G8B8A8_UNORM:
                    GL.VertexAttribPointer(attributeLocation, 4, VertexAttribPointerType.UnsignedByte, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R32G32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.Float, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R16G16_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.HalfFloat, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R32G32B32A32_FLOAT:
                    GL.VertexAttribPointer(attributeLocation, 4, VertexAttribPointerType.Float, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R8G8B8A8_UINT:
                    GL.VertexAttribPointer(attributeLocation, 4, VertexAttribPointerType.UnsignedByte, false, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R16G16_SINT:
                    GL.VertexAttribIPointer(attributeLocation, 2, VertexAttribIntegerType.Short, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R16G16B16A16_SINT:
                    GL.VertexAttribIPointer(attributeLocation, 4, VertexAttribIntegerType.Short, stride, (IntPtr)attribute.Offset);
                    break;

                case DXGI_FORMAT.R16G16_UNORM:
                    GL.VertexAttribPointer(attributeLocation, 2, VertexAttribPointerType.UnsignedShort, true, stride, (IntPtr)attribute.Offset);
                    break;

                default:
                    throw new Exception("Unknown attribute format " + attribute.Type);
            }
        }

        public static void WriteObject(StreamWriter objStream, StreamWriter mtlStream, string mtlFilename, Resource resource)
        {
            var mesh = (VBIB)resource.Blocks[BlockType.VBIB];

            const string header = "# Written by VRF - https://opensource.steamdb.info/ValveResourceFormat/";

            mtlStream.WriteLine(header);
            objStream.WriteLine(header);
            objStream.WriteLine($"# Vertex buffers: {mesh.VertexBuffers.Count}");
            objStream.WriteLine($"# Index buffers: {mesh.IndexBuffers.Count}");
            objStream.WriteLine($"mtllib {mtlFilename}.mtl");

            if (mesh.VertexBuffers.Count != mesh.IndexBuffers.Count)
            {
                throw new InvalidDataException("VertexBuffers.Count != IndexBuffers.Count");
            }

            var data = (BinaryKV3)resource.Blocks[BlockType.DATA];
            var sceneObjects = (KVObject)data.Data.Properties["m_sceneObjects"].Value;

            var indexCount = 1;

            for (var b = 0; b < sceneObjects.Properties.Count; b++)
            {
                var drawCalls = (KVObject)((KVObject)sceneObjects.Properties[b.ToString()].Value).Properties["m_drawCalls"].Value;

                for (var i = 0; i < drawCalls.Properties.Count; i++)
                {
                    var d = (KVObject)drawCalls.Properties[i.ToString()].Value;
                    var materialName = d.Properties["m_material"].Value.ToString();

                    var groupName = Path.GetFileNameWithoutExtension(materialName);
                    mtlStream.WriteLine($"newmtl {groupName}");
                    mtlStream.WriteLine("illum 2");
                    mtlStream.WriteLine($"map_Ka {groupName}.png");
                    mtlStream.WriteLine($"map_Kd {groupName}.png");

                    var f = (KVObject)d.Properties["m_indexBuffer"].Value;
                    var indexBufferId = Convert.ToUInt32(f.Properties["m_hBuffer"].Value);

                    var g = (KVObject)d.Properties["m_vertexBuffers"].Value;
                    var h = (KVObject)g.Properties["0"].Value; // TODO: Not just 0
                    var vertexBufferId = Convert.ToUInt32(h.Properties["m_hBuffer"].Value);

                    var vertexBuffer = mesh.VertexBuffers[(int)vertexBufferId];
                    objStream.WriteLine($"# Vertex Buffer {i}. Count: {vertexBuffer.Count}, Size: {vertexBuffer.Size}");
                    for (var j = 0; j < vertexBuffer.Count; j++)
                    {
                        foreach (var attribute in vertexBuffer.Attributes)
                        {
                            var result = mesh.ReadVertexAttribute(j, vertexBuffer, attribute);

                            switch (attribute.Name)
                            {
                                case "POSITION":
                                    objStream.WriteLine($"v {result[0]:F6} {result[1]:F6} {result[2]:F6}");
                                    break;

                                case "NORMAL":
                                    objStream.WriteLine($"vn {result[0]:F6} {result[1]:F6} {result[2]:F6}");
                                    break;

                                case "TEXCOORD":
                                    objStream.WriteLine($"vt {result[0]:F6} {result[1]:F6}");
                                    break;
                            }
                        }
                    }

                    var indexBuffer = mesh.IndexBuffers[(int)indexBufferId];

                    objStream.WriteLine($"# Index Buffer {i}. Count: {indexBuffer.Count}, Size: {indexBuffer.Size}");

                    objStream.WriteLine($"g {groupName}");
                    objStream.WriteLine($"usemtl {groupName}");

                    if (indexBuffer.Size == 2)
                    {
                        var indexArray = new ushort[indexBuffer.Count];
                        Buffer.BlockCopy(indexBuffer.Buffer, 0, indexArray, 0, indexBuffer.Buffer.Length);

                        for (var j = 0; j < indexBuffer.Count; j += 3)
                        {
                            objStream.WriteLine($"f {indexArray[j] + indexCount}/{indexArray[j] + indexCount}/{indexArray[j] + indexCount} {indexArray[j + 1] + indexCount}/{indexArray[j + 1] + indexCount}/{indexArray[j + 1] + indexCount} {indexArray[j + 2] + indexCount}/{indexArray[j + 2] + indexCount}/{indexArray[j + 2] + indexCount}");
                        }
                    }
                    else if (indexBuffer.Size == 4)
                    {
                        var indexArray = new uint[indexBuffer.Count];
                        Buffer.BlockCopy(indexBuffer.Buffer, 0, indexArray, 0, indexBuffer.Buffer.Length);

                        for (var j = 0; j < indexBuffer.Count; j += 3)
                        {
                            objStream.WriteLine($"f {indexArray[j] + indexCount}/{indexArray[j] + indexCount}/{indexArray[j] + indexCount} {indexArray[j + 1] + indexCount}/{indexArray[j + 1] + indexCount}/{indexArray[j + 1] + indexCount} {indexArray[j + 2] + indexCount}/{indexArray[j + 2] + indexCount}/{indexArray[j + 2] + indexCount}");
                        }
                    }
                    else
                    {
                        throw new InvalidDataException("Index size isn't 2 or 4, dafuq.");
                    }

                    indexCount += (int)vertexBuffer.Count;
                }
            }
        }

        public static void ExportFbx(string fileName, Resource resource)
        {
            var mtlFilename = Path.GetFileNameWithoutExtension(fileName);
            var mesh = (VBIB)resource.Blocks[BlockType.VBIB];

            const string header = "# Written by VRF - https://opensource.steamdb.info/ValveResourceFormat/";

            if (mesh.VertexBuffers.Count != mesh.IndexBuffers.Count)
            {
                throw new InvalidDataException("VertexBuffers.Count != IndexBuffers.Count");
            }

            var data = (BinaryKV3)resource.Blocks[BlockType.DATA];
            var sceneObjects = (KVObject)data.Data.Properties["m_sceneObjects"].Value;

            var indexCount = 1;

            FBXManager managerInstance = FBXManager.Create();
            FBXIOSettings ios = FBXIOSettings.Create(managerInstance, "IOSRoot");
            managerInstance.SetIOSettings(ios);
            FBXScene scene = FBXScene.Create(managerInstance, mtlFilename);
            //FBXExporter

            for (var b = 0; b < sceneObjects.Properties.Count; b++)
            {

                var sceneObjectsNode = FBXNode.Create(scene, "sceneObjects" + b);

                var drawCalls = (KVObject)((KVObject)sceneObjects.Properties[b.ToString()].Value).Properties["m_drawCalls"].Value;

                for (var i = 0; i < drawCalls.Properties.Count; i++)
                {
                    var fbxMesh = FBXMesh.Create(scene, "Mesh" + i);
                    var d = (KVObject)drawCalls.Properties[i.ToString()].Value;
                    var materialName = d.Properties["m_material"].Value.ToString();

                    var groupName = Path.GetFileNameWithoutExtension(materialName);

                    var f = (KVObject)d.Properties["m_indexBuffer"].Value;
                    var indexBufferId = Convert.ToUInt32(f.Properties["m_hBuffer"].Value);

                    var g = (KVObject)d.Properties["m_vertexBuffers"].Value;
                    var h = (KVObject)g.Properties["0"].Value; // TODO: Not just 0
                    var vertexBufferId = Convert.ToUInt32(h.Properties["m_hBuffer"].Value);

                    var vertexBuffer = mesh.VertexBuffers[(int)vertexBufferId];
                    fbxMesh.InitControlPoints((int)vertexBuffer.Count);

                    var fbxNormals = fbxMesh.CreateElementNormal();
                    fbxNormals.SetMappingMode(ArcManagedFBX.Types.EMappingMode.eByControlPoint);
                    fbxNormals.SetReferenceMode(ArcManagedFBX.Types.EReferenceMode.eDirect);

                    var fbxTextCoord = fbxMesh.CreateElementUV();
                    fbxTextCoord.SetMappingMode(ArcManagedFBX.Types.EMappingMode.eByControlPoint);
                    fbxTextCoord.SetReferenceMode(ArcManagedFBX.Types.EReferenceMode.eDirect);

                    for (var j = 0; j < vertexBuffer.Count; j++)
                    {
                        foreach (var attribute in vertexBuffer.Attributes)
                        {
                            var result = mesh.ReadVertexAttribute(j, vertexBuffer, attribute);

                            switch (attribute.Name)
                            {
                                case "POSITION":
                                    fbxMesh.SetControlPointAt(new FBXVector(result[0], result[1], result[2], 1.0), j);
                                    break;

                                case "NORMAL":
                                    fbxNormals.AddDirectArrayVector(new FBXVector(result[0], result[1], result[2], 1.0));
                                    break;

                                case "TEXCOORD":
                                    fbxTextCoord.AddDirectArrayVector2D(new FBXVector(result[0], result[1], 0, 1.0));
                                    break;
                            }
                        }
                    }

                    var indexBuffer = mesh.IndexBuffers[(int)indexBufferId];

                    if (indexBuffer.Size == 2)
                    {
                        var indexArray = new ushort[indexBuffer.Count];
                        Buffer.BlockCopy(indexBuffer.Buffer, 0, indexArray, 0, indexBuffer.Buffer.Length);

                        for (var j = 0; j < indexBuffer.Count; j += 3)
                        {
                            fbxMesh.BeginPolygon(-1, -1, -1, false);
                            fbxMesh.AddPolygon(indexArray[j], -1);
                            fbxMesh.AddPolygon(indexArray[j + 1], -1);
                            fbxMesh.AddPolygon(indexArray[j + 2], -1);
                            fbxMesh.EndPolygon();
                        }
                    }
                    else if (indexBuffer.Size == 4)
                    {
                        var indexArray = new uint[indexBuffer.Count];
                        Buffer.BlockCopy(indexBuffer.Buffer, 0, indexArray, 0, indexBuffer.Buffer.Length);

                        for (var j = 0; j < indexBuffer.Count; j += 3)
                        {
                            fbxMesh.BeginPolygon(-1, -1, -1, false);
                            fbxMesh.AddPolygon((int)indexArray[j], -1);
                            fbxMesh.AddPolygon((int)indexArray[j + 1], -1);
                            fbxMesh.AddPolygon((int)indexArray[j + 2], -1);
                            fbxMesh.EndPolygon();
                        }
                    }
                    else
                    {
                        throw new InvalidDataException("Index size isn't 2 or 4, dafuq.");
                    }

                    indexCount += (int)vertexBuffer.Count;

                    var objectNode = FBXNode.Create(scene, "DrawCall" + i);
                    objectNode.SetNodeAttribute(fbxMesh);
                    sceneObjectsNode.AddChild(objectNode);
                }

                sceneObjectsNode.SetRotation(-90, -90, 0);
                scene.GetRootNode().AddChild(sceneObjectsNode);
            }

            var fBXExporter = FBXExporter.Create(managerInstance, string.Empty);
            if (!fBXExporter.Initialize(fileName, -1, managerInstance.GetIOSettings()))
            {
                return;
            }

            if (!fBXExporter.Export(scene, false))
            {
                return;
            }

            fBXExporter.Destroy();
        }
    }

}
