using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-10-02) — 설치된 URP Sprite Lit 템플릿에 재 색상 노드만 더한다.
/// 비공개 Shader Graph API에 의존하지 않고 템플릿의 조명·알파·텍스처 연결을 보존한다.
/// </summary>
internal static class AshBossIntroShaderBuilder
{
    internal const string GraphPath = "Assets/Project/Art/Materials/BossIntroAsh.shadergraph";
    private const string MaterialPath = "Assets/Project/Art/Materials/BossIntroAsh.mat";

    /// <summary>이미 조정한 그래프와 재질은 그대로 두고 처음 필요한 에셋만 만든다.</summary>
    internal static Material Build()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GraphPath));
        if (!File.Exists(GraphPath)) CreateGraph();
        AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceSynchronousImport);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(GraphPath);
        if (shader == null || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("[보스 등장] 잿빛 Shader Graph가 정상 컴파일되지 않았다.");

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "BossIntroAsh" };
            material.SetFloat("_Ash", 1f);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        return material;
    }

    /// <summary>
    /// 추가 생성(2026-10-02) — RGB를 회색으로 바꾸고 흰색 쪽으로 35% 올린 뒤 _Ash로 섞는다.
    /// 단순 곱셈으로는 어두운 갑옷을 밝은 재로 바꿀 수 없어서 두 번의 보간이 필요하다.
    /// </summary>
    private static void CreateGraph()
    {
        const string packageAsset = "Packages/com.unity.render-pipelines.universal";
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packageAsset);
        if (package == null) throw new InvalidOperationException("URP 패키지 경로를 찾을 수 없다.");
        string template = Path.Combine(package.resolvedPath,
            "Editor/ShaderGraph/GraphTemplates/2D/0_2D Sprite Lit.shadergraph");
        List<string> objects = Regex.Matches(File.ReadAllText(template), @"(?ms)^\{.*?^\}")
            .Cast<Match>().Select(match => match.Value).ToList();
        if (objects.Count < 2) throw new InvalidOperationException("Sprite Lit 템플릿 형식이 바뀌었다.");

        string graph = objects[0];
        NodeInfo baseColor = objects.Select(JsonUtility.FromJson<NodeInfo>)
            .First(node => node.m_Name == "SurfaceDescription.BaseColor");
        var edgeData = JsonUtility.FromJson<EdgeData>(graph);
        Edge baseEdge = edgeData.m_Edges.First(edge => edge.m_InputSlot.m_Node.m_Id == baseColor.m_ObjectId);
        SlotReference originalColor = baseEdge.m_OutputSlot;
        string property = NewId();
        string propertyNode = NewId();
        string saturation = NewId();
        string brighten = NewId();
        string blend = NewId();
        var newObjects = new List<string>();

        newObjects.Add(JsonUtility.ToJson(new FloatProperty(property), true));
        AddNode(newObjects, propertyNode, "PropertyNode", "Ash", -100f, -180f,
            new[] { MakeSlot("Vector1MaterialSlot", 0, "Out", true, 1f) }, property);
        AddNode(newObjects, saturation, "SaturationNode", "Saturation", 50f, 180f,
            new[] { MakeSlot("Vector3MaterialSlot", 0, "In", false, 0f),
                MakeSlot("Vector1MaterialSlot", 1, "Saturation", false, 0f),
                MakeSlot("Vector3MaterialSlot", 2, "Out", true, 0f) });
        AddNode(newObjects, brighten, "LerpNode", "Lerp", 280f, 180f,
            new[] { MakeSlot("DynamicVectorMaterialSlot", 0, "A", false, 0f),
                MakeSlot("DynamicVectorMaterialSlot", 1, "B", false, 1f),
                MakeSlot("DynamicVectorMaterialSlot", 2, "T", false, 0.35f),
                MakeSlot("DynamicVectorMaterialSlot", 3, "Out", true, 0f) });
        AddNode(newObjects, blend, "LerpNode", "Lerp", 510f, 180f,
            new[] { MakeSlot("DynamicVectorMaterialSlot", 0, "A", false, 0f),
                MakeSlot("DynamicVectorMaterialSlot", 1, "B", false, 1f),
                MakeSlot("DynamicVectorMaterialSlot", 2, "T", false, 0f),
                MakeSlot("DynamicVectorMaterialSlot", 3, "Out", true, 0f) });

        // 원래 BaseColor만 갈아 끼우므로 알파와 2D 조명 경로는 템플릿 그대로 남는다.
        baseEdge.m_OutputSlot = new SlotReference(blend, 3);
        var edges = edgeData.m_Edges.ToList();
        edges.Add(new Edge(originalColor, new SlotReference(saturation, 0)));
        edges.Add(new Edge(new SlotReference(saturation, 2), new SlotReference(brighten, 0)));
        edges.Add(new Edge(originalColor, new SlotReference(blend, 0)));
        edges.Add(new Edge(new SlotReference(brighten, 3), new SlotReference(blend, 1)));
        edges.Add(new Edge(new SlotReference(propertyNode, 0), new SlotReference(blend, 2)));
        string edgeJson = JsonUtility.ToJson(new EdgeData { m_Edges = edges.ToArray() }, true);
        edgeJson = edgeJson.Substring(1, edgeJson.Length - 2).Trim();
        graph = Regex.Replace(graph, @"""m_Edges""\s*:\s*\[.*?\](?=\s*,\s*""m_VertexContext"")",
            _ => edgeJson, RegexOptions.Singleline);
        graph = InsertReferences(graph, "m_Properties", property);
        graph = InsertReferences(graph, "m_Nodes", propertyNode, saturation, brighten, blend);
        graph = graph.Replace("\"m_Path\": \"Shader Graphs\"", "\"m_Path\": \"Path of Ash\"");
        graph = Regex.Replace(graph, @"(""m_FragmentContext""\s*:\s*\{\s*""m_Position""\s*:\s*\{\s*""x""\s*:)\s*[-\d.]+", "$1 780.0");
        objects[0] = graph;
        for (int i = 1; i < objects.Count; i++)
        {
            if (JsonUtility.FromJson<NodeInfo>(objects[i]).m_Type == "UnityEditor.ShaderGraph.CategoryData")
                objects[i] = InsertReferences(objects[i], "m_ChildObjectList", property);
        }
        objects.AddRange(newObjects);
        File.WriteAllText(GraphPath, string.Join("\n\n", objects) + "\n", new System.Text.UTF8Encoding(false));
    }

    /// <summary>추가할 참조를 배열 앞에 넣어 템플릿의 알 수 없는 나머지 필드를 보존한다.</summary>
    private static string InsertReferences(string json, string field, params string[] ids)
    {
        string references = string.Join(",", ids.Select(id => JsonUtility.ToJson(new Reference(id))));
        return Regex.Replace(json, "(\"" + field + "\"\\s*:\\s*\\[)", "$1" + references + ",", RegexOptions.None);
    }

    /// <summary>노드와 슬롯을 별도 JSON 오브젝트로 저장하는 Shader Graph 형식을 따른다.</summary>
    private static void AddNode(List<string> objects, string id, string type, string name,
        float x, float y, string[] slots, string property = null)
    {
        var ids = slots.Select(slot => JsonUtility.FromJson<NodeInfo>(slot).m_ObjectId).ToArray();
        objects.Add(JsonUtility.ToJson(new Node
        {
            m_ObjectId = id, m_Type = "UnityEditor.ShaderGraph." + type, m_Name = name,
            m_DrawState = new DrawState { m_Position = new Position { x = x, y = y } },
            m_Slots = ids.Select(slotId => new Reference(slotId)).ToArray(),
            m_Property = new Reference(property ?? string.Empty)
        }, true));
        objects.AddRange(slots);
    }

    /// <summary>스칼라와 벡터 슬롯은 값의 JSON 모양이 달라서 타입별로 직렬화한다.</summary>
    private static string MakeSlot(string type, int index, string name, bool output, float value)
    {
        string valueJson = type == "Vector1MaterialSlot" ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : JsonUtility.ToJson(new VectorValue { x = value, y = value, z = value, w = value });
        return "{\"m_SGVersion\":0,\"m_Type\":\"UnityEditor.ShaderGraph." + type +
            "\",\"m_ObjectId\":\"" + NewId() + "\",\"m_Id\":" + index +
            ",\"m_DisplayName\":\"" + name + "\",\"m_SlotType\":" + (output ? 1 : 0) +
            ",\"m_Hidden\":false,\"m_ShaderOutputName\":\"" + name +
            "\",\"m_StageCapability\":3,\"m_Value\":" + valueJson + ",\"m_DefaultValue\":" + valueJson + "}";
    }

    /// <summary>노드의 영구 식별자가 서로 겹치지 않게 생성한다.</summary>
    private static string NewId() => Guid.NewGuid().ToString("N");

    // 추가 생성(2026-10-02) — JsonUtility용 자료형은 템플릿에서 읽는 최소 필드만 가진다.
    [Serializable] private class NodeInfo { public string m_Type; public string m_ObjectId; public string m_Name; }
    [Serializable] private class Reference
    {
        public string m_Id;
        /// <summary>Shader Graph 오브젝트 참조를 만든다.</summary>
        public Reference(string id) { m_Id = id; }
    }
    [Serializable] private class SlotReference
    {
        public Reference m_Node;
        public int m_SlotId;
        /// <summary>노드의 특정 입출력 슬롯을 가리킨다.</summary>
        public SlotReference(string id, int slot) { m_Node = new Reference(id); m_SlotId = slot; }
    }
    [Serializable] private class Edge
    {
        public SlotReference m_OutputSlot;
        public SlotReference m_InputSlot;
        /// <summary>출력에서 입력으로 이어지는 선을 만든다.</summary>
        public Edge(SlotReference output, SlotReference input) { m_OutputSlot = output; m_InputSlot = input; }
    }
    [Serializable] private class EdgeData { public Edge[] m_Edges; }
    [Serializable] private class VectorValue { public float x, y, z, w; }
    [Serializable] private class Position { public float x, y; public float width = 200f, height = 160f; }
    [Serializable] private class DrawState { public bool m_Expanded = true; public Position m_Position; }
    [Serializable] private class Node
    {
        public int m_SGVersion;
        public string m_Type, m_ObjectId, m_Name;
        public Reference m_Group = new Reference("");
        public DrawState m_DrawState;
        public Reference[] m_Slots;
        public int m_Precision;
        public bool m_PreviewExpanded = true;
        public Reference m_Property;
    }
    [Serializable] private class PropertyGuid { public string m_GuidSerialized; }
    [Serializable] private class RangeValue { public float x; public float y = 1f; }
    [Serializable] private class FloatProperty
    {
        public int m_SGVersion = 1;
        public string m_Type = "UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty";
        public string m_ObjectId;
        public PropertyGuid m_Guid = new PropertyGuid { m_GuidSerialized = Guid.NewGuid().ToString() };
        public string m_Name = "Ash";
        public int m_DefaultRefNameVersion = 1;
        public string m_RefNameGeneratedByDisplayName = "Ash";
        public string m_DefaultReferenceName = "_Ash";
        public string m_OverrideReferenceName = "_Ash";
        public bool m_GeneratePropertyBlock = true;
        public bool m_Hidden;
        public float m_Value = 1f;
        public int m_FloatType = 1;
        public RangeValue m_RangeValues = new RangeValue();
        /// <summary>Inspector에서 0~1 슬라이더로 보이는 재 덮임 값을 만든다.</summary>
        public FloatProperty(string id) { m_ObjectId = id; }
    }
}
