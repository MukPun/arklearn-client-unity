using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class SphereWireframe : MonoBehaviour
{
    [Header("基础设置")]
    public int pointCount = 200;       // 点数量
    public float radius = 1f;        // 球半径（默认0.5匹配Unity Sphere网格）
    public int maxConnections = 6;     // 每个点最多连接数

    public float width = 0.5f;         // 线的宽度

    [Header("旋转动画")]
    public bool autoRotate = true;     // 自动旋转
    public Vector3 rotateSpeed = new Vector3(0, 30, 0); // 旋转速度

    [Header("外观")]
    public Color pointColor = Color.cyan;
    public Color lineColor = new Color(0, 1, 1, 0.6f);

    [Header("调试信息")]
    public bool showDebugInfo = false;  // 是否在控制台显示调试信息

    public List<GameObject> Points => points;  // 公开访问点列表
    public Vector3[] PointPositions => pointPositions;  // 公开访问点位置数组
    public float ActualRadius => pointPositions.Length > 0 ? pointPositions[0].magnitude : radius;

    private List<GameObject> points = new List<GameObject>();
    private LineRenderer lineRenderer;
    private Vector3[] pointPositions;

    void Start()
    {
        CreateSpherePoints();
        CreateLines();
        SetupLineRenderer();

        if (showDebugInfo)
        {
            Debug.Log($"[SphereWireframe] 点数量: {pointCount}, 实际半径: {ActualRadius:F4}");
            Debug.Log($"[SphereWireframe] 点位置示例 (前5个):");
            for (int i = 0; i < Mathf.Min(5, pointPositions.Length); i++)
            {
                Debug.Log($"  点{i}: {pointPositions[i]} (距离圆心: {pointPositions[i].magnitude:F4})");
            }
        }
    }

    void Update()
    {
        if (autoRotate)
        {
            transform.Rotate(rotateSpeed * Time.deltaTime);
        }

        // 点大小呼吸动画
        foreach (GameObject point in points)
        {
            // 用点的位置坐标计算 phase，确保每个点有不同的相位
            Vector3 pos = point.transform.localPosition;
            float phase = pos.x * 3f + pos.y * 5f + pos.z * 7f;
            float breathe = 0.5f + 0.5f * Mathf.Sin(Time.time * 2f + phase);
            point.transform.localScale = Vector3.one * 0.03f * breathe;

        }
    }

    // 生成球面上均匀分布的点（Fibonacci 螺旋算法）
    void CreateSpherePoints()
    {
        pointPositions = new Vector3[pointCount];
        float goldenRatio = (1f + Mathf.Sqrt(5f)) / 2f;

        for (int i = 0; i < pointCount; i++)
        {
            float t = (float)i / pointCount;
            float inclination = Mathf.Acos(1f - 2f * t);  // 极角 [0, PI]
            float azimuth = 2f * Mathf.PI * i / goldenRatio; // 方位角 [0, 2PI]

            float x = radius * Mathf.Sin(inclination) * Mathf.Cos(azimuth);
            float y = radius * Mathf.Sin(inclination) * Mathf.Sin(azimuth);
            float z = radius * Mathf.Cos(inclination);

            pointPositions[i] = new Vector3(x, y, z);

            GameObject point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            point.name = "Point";
            point.transform.parent = transform;
            point.transform.localPosition = pointPositions[i];
            point.transform.localScale = Vector3.one * 0.03f;
            Destroy(point.GetComponent<SphereCollider>());

            var renderer = point.GetComponent<Renderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.material.color = pointColor;

            points.Add(point);
        }
    }

    // 计算两个点之间的角距离
    float AngularDistance(Vector3 a, Vector3 b)
    {
        return Mathf.Acos(Mathf.Clamp(Vector3.Dot(a.normalized, b.normalized), -1f, 1f));
    }

    // 连接相邻点生成线条（每个点最多连接 maxConnections 条）
    void CreateLines()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 0;
        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.useWorldSpace = false;
        lineRenderer.loop = false;

        List<Vector3> linePositions = new List<Vector3>();
        HashSet<long> addedLines = new HashSet<long>(); // 防止重复添加

        for (int i = 0; i < pointCount; i++)
        {
            // 找到与当前点角距离最近的 maxConnections 个点
            List<int> nearestIndices = new List<int>();

            for (int j = 0; j < pointCount; j++)
            {
                if (j == i) continue;

                float dist = AngularDistance(pointPositions[i], pointPositions[j]);

                // 插入到排序列表中
                int insertIndex = nearestIndices.Count;
                for (int k = 0; k < nearestIndices.Count; k++)
                {
                    if (dist < AngularDistance(pointPositions[i], pointPositions[nearestIndices[k]]))
                    {
                        insertIndex = k;
                        break;
                    }
                }

                if (insertIndex < maxConnections)
                {
                    nearestIndices.Insert(insertIndex, j);
                    if (nearestIndices.Count > maxConnections)
                    {
                        nearestIndices.RemoveAt(maxConnections);
                    }
                }
            }

            // 添加线条（只从序号小的点添加，避免重复）
            foreach (int neighborIndex in nearestIndices)
            {
                int smaller = Mathf.Min(i, neighborIndex);
                int larger = Mathf.Max(i, neighborIndex);
                long lineKey = (long)smaller << 32 | (uint)larger;

                if (!addedLines.Contains(lineKey))
                {
                    addedLines.Add(lineKey);
                    linePositions.Add(pointPositions[i]);
                    linePositions.Add(pointPositions[neighborIndex]);
                }
            }
        }

        lineRenderer.positionCount = linePositions.Count;
        lineRenderer.SetPositions(linePositions.ToArray());
    }

    void SetupLineRenderer()
    {
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.material.color = lineColor;
    }
}