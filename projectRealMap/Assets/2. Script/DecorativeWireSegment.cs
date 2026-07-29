using System.Collections.Generic;
using UnityEngine;

// 전선 한 가닥의 배치 정보
// 이동용 전선을 기준으로 좌우/상하로 얼마나 떨어뜨릴지 정한다
[System.Serializable]
public class DecorativeStrand
{
    [Tooltip("이 가닥을 생성할지 여부입니다. 끄면 이 줄만 사라집니다.")]
    public bool enabled = true;

    [Tooltip("전선 이름입니다. 인스펙터에서 구분용으로만 사용합니다.")]
    public string strandName = "Strand";

    [Header("시작 전봇대에서 뽑는 위치")]
    [Tooltip("전선 진행 방향 기준 좌우 오프셋입니다. 양수가 오른쪽입니다.")]
    public float startLateralOffset = 0.6f;

    [Tooltip("위아래 오프셋입니다. 양수가 위쪽입니다.")]
    public float startVerticalOffset = 0f;

    [Header("도착 전봇대에 걸리는 위치")]
    public float endLateralOffset = 0.6f;
    public float endVerticalOffset = 0f;

    [Header("처짐")]
    [Tooltip("이 가닥만의 처짐 정도입니다. 가닥마다 다르게 주면 자연스러워집니다.")]
    public float sagAmount = 0.5f;
}

// 이동용 전선(AutoWireSegment) 옆에 붙는 장식용 전선
// 이동에는 관여하지 않고 메쉬로만 생성되므로 매 프레임 비용이 없다
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class DecorativeWireSegment : MonoBehaviour
{
    [Header("연결 노드")]
    public PoleNode startNode;
    public PoleNode endNode;

    [Header("노드 대신 Transform으로 지정")]
    [Tooltip("이동용 전봇대가 아닌 장식 전용 전봇대(DummyPoleNode)를 이을 때 사용합니다.")]
    public Transform startPointOverride;
    public Transform endPointOverride;

    [Header("장식 전선 전체 사용 여부")]
    [Tooltip("끄면 이 위치의 장식 전선이 전부 사라집니다. 이동용 전선은 그대로 남습니다.")]
    public bool useDecorativeWires = true;

    [Header("가닥 설정")]
    [Tooltip("가닥을 추가하면 그만큼 전선이 늘어납니다. 가운데는 이동용 전선이 차지하므로 비워둡니다.")]
    public List<DecorativeStrand> strands = new List<DecorativeStrand>();

    [Header("전선 모양")]
    [Tooltip("전선을 몇 개의 마디로 나눠 그릴지 정합니다. 높을수록 곡선이 부드럽습니다.")]
    [Range(2, 64)]
    public int segmentCount = 12;

    [Tooltip("전선 단면을 몇 각형으로 만들지 정합니다. 3~4면 충분합니다.")]
    [Range(3, 12)]
    public int sideCount = 4;

    [Tooltip("전선 굵기(반지름)입니다.")]
    public float wireRadius = 0.03f;

    [Header("머티리얼")]
    public Material wireMaterial;

    // 생성한 메쉬를 구분하기 위한 이름
    private const string GeneratedMeshName = "DecorativeWireMesh";

    // 머티리얼을 지정하지 않았을 때 공용으로 쓸 대체 머티리얼
    // 전선마다 새로 만들면 낭비이므로 하나만 만들어 공유한다
    private static Material fallbackMaterial;

    // 네트워크 빌더가 전선을 만들면서 호출한다
    public void Initialize(
        PoleNode newStartNode,
        PoleNode newEndNode,
        List<DecorativeStrand> strandTemplate,
        int newSegmentCount,
        int newSideCount,
        float newWireRadius,
        Material newWireMaterial
    )
    {
        startNode = newStartNode;
        endNode = newEndNode;
        segmentCount = newSegmentCount;
        sideCount = newSideCount;
        wireRadius = newWireRadius;
        wireMaterial = newWireMaterial;

        strands = CopyStrands(strandTemplate);

        Rebuild();
    }

    // 장식 전용 전봇대(DummyPoleNode)끼리 이을 때 사용한다
    // 이동용 노드가 없으므로 Transform만으로 양 끝을 지정한다
    public void InitializeFromTransforms(
        Transform newStartPoint,
        Transform newEndPoint,
        List<DecorativeStrand> strandTemplate,
        int newSegmentCount,
        int newSideCount,
        float newWireRadius,
        Material newWireMaterial
    )
    {
        startNode = null;
        endNode = null;
        startPointOverride = newStartPoint;
        endPointOverride = newEndPoint;

        segmentCount = newSegmentCount;
        sideCount = newSideCount;
        wireRadius = newWireRadius;
        wireMaterial = newWireMaterial;

        strands = CopyStrands(strandTemplate);

        Rebuild();
    }

    // 이동용 노드가 있으면 그것을, 없으면 Transform 지정을 사용한다
    private bool TryGetEndpoints(out Vector3 start, out Vector3 end)
    {
        if (startNode != null && endNode != null)
        {
            start = startNode.Position;
            end = endNode.Position;
            return true;
        }

        if (startPointOverride != null && endPointOverride != null)
        {
            start = startPointOverride.position;
            end = endPointOverride.position;
            return true;
        }

        start = Vector3.zero;
        end = Vector3.zero;
        return false;
    }

    // 템플릿을 그대로 참조하면 한 곳을 수정할 때 전부 바뀌므로 복사해서 사용
    private static List<DecorativeStrand> CopyStrands(List<DecorativeStrand> strandTemplate)
    {
        List<DecorativeStrand> result = new List<DecorativeStrand>();

        if (strandTemplate == null)
        {
            return result;
        }

        foreach (DecorativeStrand source in strandTemplate)
        {
            if (source == null)
            {
                continue;
            }

            DecorativeStrand copy = new DecorativeStrand();
            copy.enabled = source.enabled;
            copy.strandName = source.strandName;
            copy.startLateralOffset = source.startLateralOffset;
            copy.startVerticalOffset = source.startVerticalOffset;
            copy.endLateralOffset = source.endLateralOffset;
            copy.endVerticalOffset = source.endVerticalOffset;
            copy.sagAmount = source.sagAmount;

            result.Add(copy);
        }

        return result;
    }

    [ContextMenu("장식 전선 다시 생성")]
    public void Rebuild()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();

        if (meshFilter == null || meshRenderer == null)
        {
            return;
        }

        // 이전에 만들어 둔 메쉬가 있으면 정리해 누수를 막는다
        ClearGeneratedMesh(meshFilter);

        if (!useDecorativeWires || strands == null)
        {
            meshRenderer.enabled = false;
            return;
        }

        if (!TryGetEndpoints(out Vector3 startBase, out Vector3 endBase))
        {
            meshRenderer.enabled = false;
            return;
        }

        // 전선 진행 방향을 기준으로 좌우 방향을 구한다
        // 전봇대가 어떻게 회전해 있든 항상 전선 기준으로 좌우가 잡히도록 하기 위함
        Vector3 wireDirection = endBase - startBase;
        wireDirection.y = 0f;

        if (wireDirection.sqrMagnitude <= 0.0001f)
        {
            meshRenderer.enabled = false;
            return;
        }

        wireDirection.Normalize();
        Vector3 lateralDirection = Vector3.Cross(Vector3.up, wireDirection).normalized;

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        foreach (DecorativeStrand strand in strands)
        {
            if (strand == null || !strand.enabled)
            {
                continue;
            }

            Vector3 strandStart = startBase
                + lateralDirection * strand.startLateralOffset
                + Vector3.up * strand.startVerticalOffset;

            Vector3 strandEnd = endBase
                + lateralDirection * strand.endLateralOffset
                + Vector3.up * strand.endVerticalOffset;

            AppendStrandMesh(strandStart, strandEnd, strand.sagAmount, vertices, normals, uvs, triangles);
        }

        if (triangles.Count == 0)
        {
            meshRenderer.enabled = false;
            return;
        }

        Mesh mesh = new Mesh();
        mesh.name = GeneratedMeshName;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        meshFilter.sharedMesh = mesh;

        meshRenderer.enabled = true;
        meshRenderer.sharedMaterial = ResolveMaterial();
    }

    // 시작점과 끝점을 잇는 늘어진 원통을 만들어 목록에 덧붙인다
    private void AppendStrandMesh(
        Vector3 start,
        Vector3 end,
        float sagAmount,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles
    )
    {
        int segments = Mathf.Max(2, segmentCount);
        int sides = Mathf.Max(3, sideCount);

        // 전선이 지나가는 중심선을 먼저 계산
        Vector3[] centerPoints = new Vector3[segments];

        for (int i = 0; i < segments; i++)
        {
            float t = (float)i / (segments - 1);

            Vector3 point = Vector3.Lerp(start, end, t);

            // 4t(1-t)는 양 끝에서 0, 가운데에서 1이 되므로 자연스러운 처짐이 된다
            point.y -= sagAmount * 4f * t * (1f - t);

            centerPoints[i] = point;
        }

        int baseIndex = vertices.Count;

        // 중심선을 따라가며 단면을 하나씩 배치
        for (int i = 0; i < segments; i++)
        {
            Vector3 tangent = GetTangent(centerPoints, i, segments);

            // 접선과 나란하지 않은 기준 벡터를 골라 단면 축을 만든다
            Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.99f
                ? Vector3.right
                : Vector3.up;

            Vector3 sideAxis = Vector3.Cross(tangent, reference).normalized;
            Vector3 upAxis = Vector3.Cross(sideAxis, tangent).normalized;

            float v = (float)i / (segments - 1);

            for (int j = 0; j < sides; j++)
            {
                float angle = (float)j / sides * Mathf.PI * 2f;

                Vector3 offsetDirection = sideAxis * Mathf.Cos(angle) + upAxis * Mathf.Sin(angle);

                vertices.Add(centerPoints[i] + offsetDirection * wireRadius);
                normals.Add(offsetDirection);
                uvs.Add(new Vector2((float)j / sides, v));
            }
        }

        // 이웃한 두 단면을 사각형으로 이어 삼각형 두 개씩 만든다
        for (int i = 0; i < segments - 1; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                int nextJ = (j + 1) % sides;

                int current = baseIndex + i * sides + j;
                int currentNext = baseIndex + i * sides + nextJ;
                int upper = baseIndex + (i + 1) * sides + j;
                int upperNext = baseIndex + (i + 1) * sides + nextJ;

                triangles.Add(current);
                triangles.Add(upper);
                triangles.Add(currentNext);

                triangles.Add(currentNext);
                triangles.Add(upper);
                triangles.Add(upperNext);
            }
        }
    }

    // 양 끝은 이웃한 점 하나만, 가운데는 앞뒤 점을 함께 써서 방향을 구한다
    private Vector3 GetTangent(Vector3[] points, int index, int count)
    {
        Vector3 tangent;

        if (index == 0)
        {
            tangent = points[1] - points[0];
        }
        else if (index == count - 1)
        {
            tangent = points[count - 1] - points[count - 2];
        }
        else
        {
            tangent = points[index + 1] - points[index - 1];
        }

        if (tangent.sqrMagnitude <= 0.0000001f)
        {
            return Vector3.forward;
        }

        return tangent.normalized;
    }

    private Material ResolveMaterial()
    {
        if (wireMaterial != null)
        {
            return wireMaterial;
        }

        if (fallbackMaterial != null)
        {
            return fallbackMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            return null;
        }

        fallbackMaterial = new Material(shader);
        fallbackMaterial.name = "DecorativeWireFallback";
        fallbackMaterial.color = new Color(0.05f, 0.05f, 0.05f, 1f);

        return fallbackMaterial;
    }

    private void ClearGeneratedMesh(MeshFilter meshFilter)
    {
        Mesh existing = meshFilter.sharedMesh;

        if (existing == null)
        {
            return;
        }

        // 우리가 만든 메쉬만 지운다
        if (existing.name != GeneratedMeshName)
        {
            return;
        }

        meshFilter.sharedMesh = null;

        if (Application.isPlaying)
        {
            Destroy(existing);
        }
        else
        {
            DestroyImmediate(existing);
        }
    }

#if UNITY_EDITOR
    // 인스펙터에서 값을 바꾸면 바로 반영되도록 처리
    // OnValidate 안에서는 오브젝트를 만들거나 지울 수 없어 한 프레임 뒤로 미룬다
    private void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += RebuildFromInspector;
    }

    private void RebuildFromInspector()
    {
        UnityEditor.EditorApplication.delayCall -= RebuildFromInspector;

        // 지연 호출 사이에 오브젝트가 사라졌을 수 있으므로 확인
        if (this == null)
        {
            return;
        }

        Rebuild();
    }
#endif
}
