using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

[ExecuteAlways]
[RequireComponent(typeof(SplineContainer))]
[RequireComponent(typeof(LineRenderer))]
public class AutoWireSegment : MonoBehaviour
{
    [Header("연결 시작 노드")]
    public PoleNode startNode;

    [Header("연결 끝 노드")]
    public PoleNode endNode;

    [Header("전선 처짐 정도")]
    public float wireSagAmount = 0.4f;

    [Header("전선 시각화 설정")]
    public int visualPointCount = 16;
    public float wireWidth = 0.06f;
    public Color wireColor = new Color(0.05f, 0.05f, 0.05f, 1f);
    public Material wireMaterial;

    [Header("자동 갱신")]
    public bool updateEveryFrame = true;

    private SplineContainer splineContainer;
    private LineRenderer lineRenderer;

    private void Reset()
    {
        CacheComponents();
    }

    private void Awake()
    {
        CacheComponents();
        UpdateWire();
    }

    private void OnValidate()
    {
        CacheComponents();
        UpdateWire();
    }

    private void LateUpdate()
    {
        if (!updateEveryFrame)
        {
            return;
        }

        UpdateWire();
    }

    private void CacheComponents()
    {
        if (splineContainer == null)
        {
            splineContainer = GetComponent<SplineContainer>();
        }

        if (lineRenderer == null)
        {
            lineRenderer = GetComponent<LineRenderer>();
        }
    }

    public void Initialize(
        PoleNode newStartNode,
        PoleNode newEndNode,
        float newSagAmount,
        int newVisualPointCount,
        float newWireWidth,
        Color newWireColor,
        Material newWireMaterial
    )
    {
        startNode = newStartNode;
        endNode = newEndNode;
        wireSagAmount = newSagAmount;
        visualPointCount = newVisualPointCount;
        wireWidth = newWireWidth;
        wireColor = newWireColor;
        wireMaterial = newWireMaterial;

        CacheComponents();
        SetupLineRendererMaterial();
        UpdateWire();
    }

    public void UpdateWire()
    {
        CacheComponents();

        if (startNode == null || endNode == null)
        {
            return;
        }

        if (splineContainer == null || lineRenderer == null)
        {
            return;
        }

        Vector3 startPosition = startNode.Position;
        Vector3 endPosition = endNode.Position;
        Vector3 middlePosition = (startPosition + endPosition) * 0.5f + Vector3.down * wireSagAmount;

        UpdateSpline(startPosition, middlePosition, endPosition);
        UpdateLineRenderer();
    }

    private void UpdateSpline(Vector3 startPosition, Vector3 middlePosition, Vector3 endPosition)
    {
        Spline spline = splineContainer.Spline;
        spline.Clear();

        BezierKnot startKnot = new BezierKnot(new float3(startPosition.x, startPosition.y, startPosition.z));
        BezierKnot middleKnot = new BezierKnot(new float3(middlePosition.x, middlePosition.y, middlePosition.z));
        BezierKnot endKnot = new BezierKnot(new float3(endPosition.x, endPosition.y, endPosition.z));

        spline.Add(startKnot);
        spline.Add(middleKnot);
        spline.Add(endKnot);
    }

    private void UpdateLineRenderer()
    {
        visualPointCount = Mathf.Max(2, visualPointCount);

        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = visualPointCount;

        lineRenderer.startWidth = wireWidth;
        lineRenderer.endWidth = wireWidth;

        lineRenderer.startColor = wireColor;
        lineRenderer.endColor = wireColor;

        lineRenderer.numCapVertices = 2;
        lineRenderer.numCornerVertices = 2;

        SetupLineRendererMaterial();

        for (int i = 0; i < visualPointCount; i++)
        {
            float t = (float)i / (visualPointCount - 1);
            Vector3 position = splineContainer.EvaluatePosition(t);
            lineRenderer.SetPosition(i, position);
        }
    }

    private void SetupLineRendererMaterial()
    {
        if (lineRenderer == null)
        {
            return;
        }

        if (wireMaterial != null)
        {
            lineRenderer.sharedMaterial = wireMaterial;
            return;
        }

        if (lineRenderer.sharedMaterial != null)
        {
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader != null)
        {
            lineRenderer.sharedMaterial = new Material(shader);
        }
    }
}