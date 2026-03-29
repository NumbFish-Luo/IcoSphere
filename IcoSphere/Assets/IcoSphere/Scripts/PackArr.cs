using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using UnityEngine;

namespace IcoSphere {
    // mesh所需数据打包
    public struct PackArr {
        public Vector3[] verts;
        public Tri[] tris;

        // 毗邻数据, 同时储存字典类型结构abuts和原始数据结构edgeAbuts
        private Dictionary<Edge, Abut> abuts;
        private EdgeAbut[] edgeAbuts;

        public const string ENDS_WITH = ".bytes";
        public static readonly string DEFAULT_PATH = Application.persistentDataPath + "/pack_arr" + ENDS_WITH;
        public static readonly string RES_DEFAULT_PATH = "Bin/pack_arr";

        // 计算总字节数
        // verts: 每个3个float (12字节)
        // tris: 每个3个int (12字节)
        // abuts: 会转成EdgeAbut类型, 每个4个int (16字节)
        const int VERTS_OFFSET = 3;
        const int TRIS_OFFSET = 3;
        const int ABUT_OFFSET = 4;

        // 头部信息二进制步距, 记录3个数组的长度
        const int HEADER_STRIDE = sizeof(int) * 3;

        // 二进制步距
        const int VERTS_STRIDE = sizeof(float) * VERTS_OFFSET;
        const int TRIS_STRIDE = sizeof(int) * TRIS_OFFSET;
        const int ABUT_STRIDE = sizeof(int) * ABUT_OFFSET;

        public PackArr(Pack pack) {
            verts = pack.verts?.ToArray();
            tris = pack.tris?.ToArray();
            abuts = pack.abuts;
            edgeAbuts = null;
        }

        public Dictionary<Edge, Abut> GetAbuts() {
            if (abuts == null || abuts.Count <= 0) {
                abuts = edgeAbuts?.ToDict(ea => ea.e, ea => ea.a);
            }
            return abuts;
        }

        public IEnumerator CoroutineGetAbuts() {
            abuts ??= new();
            int i = abuts.Count;
            int n = edgeAbuts.Length;
            bool showLog = true;
            int k = 100;
            while (i < n) {
                if (showLog && (i % k == 0)) {
                    Debug.Log($"解析毗邻数据: {i}/{n} ({i * 100.0f / n}%)");
                }
                EdgeAbut ea = edgeAbuts[i++];
                abuts.Add(ea.e, ea.a);
                yield return null;
            }
            Debug.Log($"解析毗邻数据完毕!");
        }

        public readonly bool IsEmpty() {
            // todo: 扩展abuts检测
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
                    Debug.LogError($"Resources中未找到文件: {resFilePath} (请确保文件放在Resources文件夹下, 并且后缀名不能为.bin, Unity无法解析.bin文件, 建议是.bytes)");
                    return pack;
                }

                byte[] bytes = binData.bytes;

                using (var ms = new MemoryStream(bytes))
                using (var reader = new BinaryReader(ms)) {
                    // 读取头部信息
                    int vertCount = reader.ReadInt32();
                    int triCount = reader.ReadInt32();
                    int abutCount = reader.ReadInt32();

                    // 读取顶点数据
                    pack.verts = new Vector3[vertCount];
                    for (int i = 0; i < vertCount; ++i) {
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        pack.verts[i] = new Vector3(x, y, z);
                    }

                    // 读取三角形数据
                    pack.tris = new Tri[triCount];
                    for (int i = 0; i < triCount; ++i) {
                        int v1 = reader.ReadInt32();
                        int v2 = reader.ReadInt32();
                        int v3 = reader.ReadInt32();
                        pack.tris[i] = new Tri(v1, v2, v3);
                    }

                    // 读取毗邻数据, 注意这里并不会对abuts字典数据进行设置, 因为特别花时间
                    pack.abuts = null;
                    pack.edgeAbuts = new EdgeAbut[abutCount];
                    for (int i = 0; i < abutCount; ++i) {
                        int v1 = reader.ReadInt32();
                        int v2 = reader.ReadInt32();
                        int t1 = reader.ReadInt32();
                        int t2 = reader.ReadInt32();
                        pack.edgeAbuts[i] = new(new(v1, v2), new(t1, t2));
                    }
                }

                Debug.Log($"Resources反序列化成功: {resFilePath}, 顶点数: {pack.verts.Length}, 三角形数: {pack.tris.Length}, 毗邻数据数: {pack.edgeAbuts.Length}");
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
                int vertsLen = pack.verts.Length;
                int trisLen = pack.tris.Length;
                int abutLen = pack.abuts.Count;

                int vertsSize = vertsLen * VERTS_STRIDE;
                int trisSize = trisLen * TRIS_STRIDE;
                int abutSize = abutLen * ABUT_STRIDE;

                long totalSize = HEADER_STRIDE + vertsSize + trisSize + abutSize;

                // 确保目录存在
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }

                using (var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Create, null, totalSize))
                using (var accessor = mmf.CreateViewAccessor()) {
                    int offset = 0;

                    // 写入头部信息
                    accessor.Write(offset, vertsLen);
                    offset += sizeof(int);
                    accessor.Write(offset, trisLen);
                    offset += sizeof(int);
                    accessor.Write(offset, abutLen);
                    offset += sizeof(int);

                    // 转换并批量写入顶点数据
                    int vertsDataLen = vertsLen * VERTS_OFFSET;
                    float[] vertsData = new float[vertsDataLen];
                    for (int i = 0; i < vertsLen; ++i) {
                        vertsData[i * VERTS_OFFSET] = pack.verts[i].x;
                        vertsData[i * VERTS_OFFSET + 1] = pack.verts[i].y;
                        vertsData[i * VERTS_OFFSET + 2] = pack.verts[i].z;
                    }

                    // 使用GCHandle固定内存并批量写入
                    GCHandle vertsHandle = GCHandle.Alloc(vertsData, GCHandleType.Pinned);
                    try {
                        accessor.WriteArray(offset, vertsData, 0, vertsDataLen);
                    } finally {
                        vertsHandle.Free();
                    }
                    offset += vertsSize;

                    // 转换并批量写入三角形数据
                    int trisDataLen = trisLen * TRIS_OFFSET;
                    int[] trisData = new int[trisDataLen];
                    for (int i = 0; i < trisLen; ++i) {
                        trisData[i * TRIS_OFFSET] = pack.tris[i].v1;
                        trisData[i * TRIS_OFFSET + 1] = pack.tris[i].v2;
                        trisData[i * TRIS_OFFSET + 2] = pack.tris[i].v3;
                    }

                    GCHandle trisHandle = GCHandle.Alloc(trisData, GCHandleType.Pinned);
                    try {
                        accessor.WriteArray(offset, trisData, 0, trisDataLen);
                    } finally {
                        trisHandle.Free();
                    }
                    offset += trisSize;

                    // 转换并批量写入毗邻数据
                    EdgeAbut[] edgeAbut = pack.abuts.ToArr((e, a) => new EdgeAbut(e, a));
                    int abutDataLen = abutLen * ABUT_OFFSET;
                    int[] abutData = new int[abutLen * ABUT_OFFSET];
                    for (int i = 0; i < abutLen; ++i) {
                        abutData[i * ABUT_OFFSET] = edgeAbut[i].e.v1;
                        abutData[i * ABUT_OFFSET + 1] = edgeAbut[i].e.v2;
                        abutData[i * ABUT_OFFSET + 2] = edgeAbut[i].a.t1;
                        abutData[i * ABUT_OFFSET + 3] = edgeAbut[i].a.t2;
                    }

                    GCHandle abutHandle = GCHandle.Alloc(abutData, GCHandleType.Pinned);
                    try {
                        accessor.WriteArray(offset, abutData, 0, abutDataLen);
                    } finally {
                        abutHandle.Free();
                    }
                }

                Debug.Log($"序列化成功: {filePath}, 顶点数: {vertsLen}, 三角形数: {trisLen}, 毗邻数据数: {abutLen}");
            } catch (Exception e) {
                Debug.LogError($"序列化失败: {e.Message}\n{e.StackTrace}");
            }
        }
    }
}
