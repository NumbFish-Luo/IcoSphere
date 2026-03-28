using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using UnityEngine;

namespace IcoSphere {
    // mesh所需数据打包
    public struct Pack {
        public List<Vector3> verts;
        public List<Tri> tris;

        public Pack(PackArr packArr) {
            verts = new List<Vector3>(packArr.verts);
            tris = new List<Tri>(packArr.tris);
        }
    }

    public struct PackArr {
        public Vector3[] verts;
        public Tri[] tris;

        public const string ENDS_WITH = ".bytes";
        public static readonly string DEFAULT_PATH = Application.persistentDataPath + "/pack_arr" + ENDS_WITH;
        public static readonly string RES_DEFAULT_PATH = "Bin/pack_arr";

        public PackArr(Pack pack) {
            verts = pack.verts.ToArray();
            tris = pack.tris.ToArray();
        }

        public readonly bool IsEmpty() {
            return verts == null || tris == null || verts?.Length <= 0 || tris?.Length <= 0;
        }

        public static string CombineFilePath(int recursion) {
            return Application.persistentDataPath + "/pack_arr_" + recursion + ENDS_WITH;
        }

        public static string ResCombineFilePath(int recursion) {
            return "Bin/pack_arr_" + recursion; // Resources中的相对路径，不需要扩展名
        }

        public static void ResSaveToBinFile(PackArr pack, string filePath = null) {
            Debug.LogError("Unity不支持运行时保存文件到Resources文件夹, 请运行不带Res开头的SaveToBinFile函数");
        }

        public static PackArr ResReadFromBinFile(string resFilePath = null) {
            resFilePath ??= RES_DEFAULT_PATH;
            PackArr pack = new();

            try {
                // 从Resources加载TextAsset, 二进制文件需要作为TextAsset导入
                TextAsset binData = Resources.Load<TextAsset>(resFilePath);

                if (binData == null) {
                    Debug.LogError($"Resources中未找到文件: {resFilePath}（请确保文件放在Resources文件夹下）");
                    return pack;
                }

                byte[] bytes = binData.bytes;

                using (var ms = new MemoryStream(bytes))
                using (var reader = new BinaryReader(ms)) {
                    // 读取头部信息
                    int vertCount = reader.ReadInt32();
                    int triCount = reader.ReadInt32();

                    // 读取顶点数据
                    pack.verts = new Vector3[vertCount];
                    for (int i = 0; i < vertCount; i++) {
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        pack.verts[i] = new Vector3(x, y, z);
                    }

                    // 读取三角形数据
                    pack.tris = new Tri[triCount];
                    for (int i = 0; i < triCount; i++) {
                        int v1 = reader.ReadInt32();
                        int v2 = reader.ReadInt32();
                        int v3 = reader.ReadInt32();
                        pack.tris[i] = new Tri(v1, v2, v3);
                    }
                }

                Debug.Log($"Resources反序列化成功: {resFilePath}, 顶点数: {pack.verts.Length}, 三角形数: {pack.tris.Length}");
                return pack;
            } catch (Exception e) {
                Debug.LogError($"Resources反序列化失败: {e.Message}\n{e.StackTrace}");
                return pack;
            }
        }

        public static void SaveToBinFile(PackArr pack, string filePath = null) {
            filePath ??= DEFAULT_PATH;
            if (pack.verts == null || pack.tris == null) {
                Debug.LogError("Pack数据为空，无法序列化");
                return;
            }

            try {
                // 计算总字节数
                // Vector3: 每个3个float (12字节)
                // Tri: 每个3个int (12字节)
                int vertsSize = pack.verts.Length * sizeof(float) * 3;
                int trisSize = pack.tris.Length * sizeof(int) * 3;
                int headerSize = sizeof(int) * 2; // 存储两个数组的长度

                long totalSize = headerSize + vertsSize + trisSize;

                // 确保目录存在
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }

                using (var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Create, null, totalSize))
                using (var accessor = mmf.CreateViewAccessor()) {
                    int offset = 0;

                    // 写入头部信息
                    accessor.Write(offset, pack.verts.Length);
                    offset += sizeof(int);
                    accessor.Write(offset, pack.tris.Length);
                    offset += sizeof(int);

                    // 转换并批量写入顶点数据
                    float[] vertsData = new float[pack.verts.Length * 3];
                    for (int i = 0; i < pack.verts.Length; i++) {
                        vertsData[i * 3] = pack.verts[i].x;
                        vertsData[i * 3 + 1] = pack.verts[i].y;
                        vertsData[i * 3 + 2] = pack.verts[i].z;
                    }

                    // 使用GCHandle固定内存并批量写入
                    GCHandle vertsHandle = GCHandle.Alloc(vertsData, GCHandleType.Pinned);
                    try {
                        accessor.WriteArray(offset, vertsData, 0, vertsData.Length);
                    } finally {
                        vertsHandle.Free();
                    }
                    offset += vertsSize;

                    // 转换并批量写入三角形数据
                    int[] trisData = new int[pack.tris.Length * 3];
                    for (int i = 0; i < pack.tris.Length; i++) {
                        trisData[i * 3] = pack.tris[i].v1;
                        trisData[i * 3 + 1] = pack.tris[i].v2;
                        trisData[i * 3 + 2] = pack.tris[i].v3;
                    }

                    GCHandle trisHandle = GCHandle.Alloc(trisData, GCHandleType.Pinned);
                    try {
                        accessor.WriteArray(offset, trisData, 0, trisData.Length);
                    } finally {
                        trisHandle.Free();
                    }
                }

                Debug.Log($"序列化成功: {filePath}, 顶点数: {pack.verts.Length}, 三角形数: {pack.tris.Length}");
            } catch (Exception e) {
                Debug.LogError($"序列化失败: {e.Message}\n{e.StackTrace}");
            }
        }

        public static PackArr ReadFromBinFile(string filePath = null) {
            filePath ??= DEFAULT_PATH;
            PackArr pack = new();

            if (!File.Exists(filePath)) {
                Debug.LogError($"文件不存在: {filePath}");
                return pack;
            }

            try {
                using (var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open))
                using (var accessor = mmf.CreateViewAccessor()) {
                    int offset = 0;

                    // 读取头部信息
                    int vertCount = accessor.ReadInt32(offset);
                    offset += sizeof(int);
                    int triCount = accessor.ReadInt32(offset);
                    offset += sizeof(int);

                    // 读取顶点数据
                    pack.verts = new Vector3[vertCount];
                    if (vertCount > 0) {
                        float[] vertsData = new float[vertCount * 3];
                        accessor.ReadArray(offset, vertsData, 0, vertsData.Length);

                        for (int i = 0; i < vertCount; i++) {
                            pack.verts[i] = new Vector3(
                                vertsData[i * 3],
                                vertsData[i * 3 + 1],
                                vertsData[i * 3 + 2]
                            );
                        }
                    }
                    offset += vertCount * sizeof(float) * 3;

                    // 读取三角形数据
                    pack.tris = new Tri[triCount];
                    if (triCount > 0) {
                        int[] trisData = new int[triCount * 3];
                        accessor.ReadArray(offset, trisData, 0, trisData.Length);

                        for (int i = 0; i < triCount; i++) {
                            pack.tris[i] = new Tri(
                                trisData[i * 3],
                                trisData[i * 3 + 1],
                                trisData[i * 3 + 2]
                            );
                        }
                    }
                }

                Debug.Log($"反序列化成功: {filePath}, 顶点数: {pack.verts.Length}, 三角形数: {pack.tris.Length}");
                return pack;
            } catch (Exception e) {
                Debug.LogError($"反序列化失败: {e.Message}\n{e.StackTrace}");
                return pack;
            }
        }
    }
}
