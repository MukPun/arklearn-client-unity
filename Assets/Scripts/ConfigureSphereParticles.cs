using UnityEngine;

public class ConfigureSphereParticles : MonoBehaviour {
    public float radius = 50f;
    public int particleCount = 200;
    public Color particleColor = new Color(0, 1f, 0.5f, 1f);

    void Start() {
        ParticleSystem ps = GetComponent<ParticleSystem>();
        if (ps == null) return;

        // Main module
        var main = ps.main;
        main.startLifetime = -1; // 无限寿命
        main.startSpeed = 0;
        main.startSize = 5f;
        main.startColor = particleColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // Shape - Sphere
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;

        // Emission - burst once
        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.rateOverDistance = 0;

        ParticleSystem.Burst burst = new ParticleSystem.Burst(0, particleCount);
        emission.SetBursts(new ParticleSystem.Burst[] { burst });

        // Renderer
        var renderer = GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = new Material(Shader.Find("Sprites/Default"));
    }
}
