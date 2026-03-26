using UnityEngine;

public class WireframeLines : MonoBehaviour {
    public int latSegments = 8;
    public int lonSegments = 12;
    public float radius = 50;
    public Color lineColor = new Color(0, 0.8f, 0.5f, 0.8f);
    public float lineWidth = 0.5f;

    void Start() {
        CreateLatLines();
        CreateLonLines();
    }

    void CreateLatLines() {
        for (int i = 1; i < latSegments; i++) {
            float theta = i * Mathf.PI / latSegments;
            CreateCircle(theta, "Latitude_" + i);
        }
    }

    void CreateLonLines() {
        for (int i = 0; i < lonSegments; i++) {
            float phi = i * 2 * Mathf.PI / lonSegments;
            CreateLongitudeLine(phi, "Longitude_" + i);
        }
    }

    void CreateCircle(float theta, string name) {
        GameObject line = new GameObject(name);
        line.transform.parent = transform;
        LineRenderer lr = line.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lineColor;
        lr.endColor = lineColor;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;

        Vector3[] points = new Vector3[64];
        for (int i = 0; i < 64; i++) {
            float phi = i * 2 * Mathf.PI / 63;
            points[i] = new Vector3(
                radius * Mathf.Sin(theta) * Mathf.Cos(phi),
                radius * Mathf.Cos(theta),
                radius * Mathf.Sin(theta) * Mathf.Sin(phi)
            );
        }
        lr.positionCount = 64;
        lr.SetPositions(points);
    }

    void CreateLongitudeLine(float phi, string name) {
        GameObject line = new GameObject(name);
        line.transform.parent = transform;
        LineRenderer lr = line.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lineColor;
        lr.endColor = lineColor;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;

        Vector3[] points = new Vector3[64];
        for (int i = 0; i < 64; i++) {
            float theta = i * Mathf.PI / 63;
            points[i] = new Vector3(
                radius * Mathf.Sin(theta) * Mathf.Cos(phi),
                radius * Mathf.Cos(theta),
                radius * Mathf.Sin(theta) * Mathf.Sin(phi)
            );
        }
        lr.positionCount = 64;
        lr.SetPositions(points);
    }
}
