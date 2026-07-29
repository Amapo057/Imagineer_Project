using System.Collections.Generic;
using UnityEngine;

// 장식 전용 전봇대(DummyPoleNode)끼리만 전선을 잇는 빌더
//
// PoleNetworkBuilder와 완전히 분리되어 있다.
// 이 빌더는 DummyPoleNode만 찾고, PoleNetworkBuilder는 PoleNode만 찾으므로
// 두 종류가 서로 연결될 방법이 없다.
// 또한 SplineContainer나 WireConnection을 전혀 만들지 않으므로 이동 로직에 잡히지 않는다.
public class DummyPoleNetworkBuilder : MonoBehaviour
{
    [Header("런타임 자동 생성")]
    [Tooltip("Play 시작 시 자동으로 장식 전선을 다시 생성합니다.")]
    public bool buildOnAwake = true;

    [Header("자동 연결 설정")]
    [Tooltip("이 거리 안에 있는 장식 전봇대만 연결 후보로 봅니다.")]
    public float maxConnectDistance = 30f;

    [Tooltip("이 거리보다 가까운 전봇대끼리는 연결하지 않습니다. 0이면 제한이 없습니다.")]
    public float minConnectDistance = 0f;

    [Tooltip("방향별 후보로 인정할 각도 기준입니다. 값이 높을수록 정면에 가까운 노드만 선택합니다.")]
    [Range(0f, 1f)]
    public float directionDotThreshold = 0.45f;

    [Tooltip("한 전봇대에서 앞/뒤/좌/우 방향별로 최대 하나씩 연결합니다.")]
    public bool useFourDirectionLimit = true;

    [Header("대각선 연결 설정")]
    [Tooltip("꺼져 있으면 상/하/좌/우에 가까운 연결만 허용합니다.")]
    public bool allowDiagonalConnections = false;

    [Tooltip("대각선 연결을 막을 때 사용하는 축 판정 값입니다.")]
    [Range(0.5f, 1f)]
    public float axisStrictness = 0.75f;

    [Header("자동 생성 오브젝트")]
    public string generatedWireParentName = "DummyGeneratedWires";

    [Tooltip("자동 생성 전에 기존 장식 전선을 삭제합니다.")]
    public bool clearOldGeneratedWires = true;

    [Header("전선 가닥 설정")]
    [Tooltip("장식 전봇대는 이동용 전선이 없으므로 가운데 가닥도 만들 수 있습니다.")]
    public List<DecorativeStrand> strands = new List<DecorativeStrand>
    {
        new DecorativeStrand
        {
            strandName = "Center",
            startLateralOffset = 0f,
            endLateralOffset = 0f,
            sagAmount = 0.45f
        },
        new DecorativeStrand
        {
            strandName = "Left",
            startLateralOffset = -0.6f,
            endLateralOffset = -0.6f,
            sagAmount = 0.5f
        },
        new DecorativeStrand
        {
            strandName = "Right",
            startLateralOffset = 0.6f,
            endLateralOffset = 0.6f,
            sagAmount = 0.5f
        }
    };

    [Header("전선 모양")]
    [Range(2, 64)]
    public int segmentCount = 12;

    [Range(3, 12)]
    public int sideCount = 4;

    public float wireRadius = 0.03f;

    public Material wireMaterial;

    [Header("선 꼬임 방지")]
    [Tooltip("새 전선이 기존 전선과 XZ 평면에서 교차하면 생성하지 않습니다.")]
    public bool preventCrossing = true;

    [Header("디버그")]
    public bool showDebugLog = true;

    private readonly List<DummyPair> createdPairs = new List<DummyPair>();

    private void Awake()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!buildOnAwake)
        {
            return;
        }

        BuildDummyNetwork();
    }

    [ContextMenu("Build Dummy Network")]
    public void BuildDummyNetwork()
    {
        DummyPoleNode[] nodes = FindObjectsByType<DummyPoleNode>(FindObjectsSortMode.None);

        if (nodes == null || nodes.Length == 0)
        {
            Debug.LogWarning("DummyPoleNode를 찾지 못했습니다.");
            return;
        }

        if (showDebugLog)
        {
            Debug.Log("장식 전선 생성 시작 / DummyPoleNode 개수: " + nodes.Length);
        }

        createdPairs.Clear();

        Transform wireParent = PrepareGeneratedWireParent();

        foreach (DummyPoleNode node in nodes)
        {
            if (node == null || !node.includeInDummyNetwork)
            {
                continue;
            }

            List<DummyPoleNode> targets = SelectTargetNodesByDirection(node, nodes);

            foreach (DummyPoleNode target in targets)
            {
                if (target == null)
                {
                    continue;
                }

                if (IsPairAlreadyCreated(node, target))
                {
                    continue;
                }

                if (preventCrossing && DoesNewConnectionCrossExisting(node, target))
                {
                    if (showDebugLog)
                    {
                        Debug.Log("선 교차로 연결 제외: " + node.name + " - " + target.name);
                    }

                    continue;
                }

                CreateDummyWire(node, target, wireParent);

                createdPairs.Add(new DummyPair(node, target));

                if (showDebugLog)
                {
                    Debug.Log("장식 전선 생성: " + node.name + " <-> " + target.name);
                }
            }
        }

        if (showDebugLog)
        {
            Debug.Log("장식 전선 생성 완료 / 개수: " + createdPairs.Count);
        }
    }

    [ContextMenu("Clear Dummy Network")]
    public void ClearDummyNetwork()
    {
        GameObject parentObject = GameObject.Find(generatedWireParentName);

        if (parentObject == null)
        {
            return;
        }

        DestroyObject(parentObject);
        createdPairs.Clear();
    }

    private Transform PrepareGeneratedWireParent()
    {
        GameObject parentObject = GameObject.Find(generatedWireParentName);

        if (parentObject == null)
        {
            parentObject = new GameObject(generatedWireParentName);
        }

        if (clearOldGeneratedWires)
        {
            List<GameObject> childrenToDelete = new List<GameObject>();

            for (int i = 0; i < parentObject.transform.childCount; i++)
            {
                childrenToDelete.Add(parentObject.transform.GetChild(i).gameObject);
            }

            foreach (GameObject child in childrenToDelete)
            {
                DestroyObject(child);
            }
        }

        return parentObject.transform;
    }

    private void CreateDummyWire(DummyPoleNode startNode, DummyPoleNode endNode, Transform parent)
    {
        string wireName = "DummyWire_" + startNode.name + "_" + endNode.name;

        GameObject wireObject = new GameObject(wireName);
        wireObject.transform.SetParent(parent);
        wireObject.transform.localPosition = Vector3.zero;
        wireObject.transform.localRotation = Quaternion.identity;
        wireObject.transform.localScale = Vector3.one;

        DecorativeWireSegment wireSegment = wireObject.AddComponent<DecorativeWireSegment>();

        // 이동용 노드가 아니므로 Transform으로 양 끝을 지정한다
        wireSegment.InitializeFromTransforms(
            startNode.WireTransform,
            endNode.WireTransform,
            strands,
            segmentCount,
            sideCount,
            wireRadius,
            wireMaterial
        );
    }

    private List<DummyPoleNode> SelectTargetNodesByDirection(DummyPoleNode origin, DummyPoleNode[] allNodes)
    {
        List<DummyPoleNode> result = new List<DummyPoleNode>();

        if (!useFourDirectionLimit)
        {
            foreach (DummyPoleNode candidate in allNodes)
            {
                if (!IsCandidateValid(origin, candidate))
                {
                    continue;
                }

                result.Add(candidate);
            }

            return result;
        }

        DummyPoleNode bestForward = null;
        DummyPoleNode bestBack = null;
        DummyPoleNode bestRight = null;
        DummyPoleNode bestLeft = null;

        float bestForwardScore = float.MinValue;
        float bestBackScore = float.MinValue;
        float bestRightScore = float.MinValue;
        float bestLeftScore = float.MinValue;

        foreach (DummyPoleNode candidate in allNodes)
        {
            if (!IsCandidateValid(origin, candidate))
            {
                continue;
            }

            Vector3 toCandidate = candidate.Position - origin.Position;
            toCandidate.y = 0f;

            float distance = toCandidate.magnitude;
            Vector3 direction = toCandidate.normalized;

            TrySetBestCandidate(direction, Vector3.forward, distance, candidate, ref bestForward, ref bestForwardScore);
            TrySetBestCandidate(direction, Vector3.back, distance, candidate, ref bestBack, ref bestBackScore);
            TrySetBestCandidate(direction, Vector3.right, distance, candidate, ref bestRight, ref bestRightScore);
            TrySetBestCandidate(direction, Vector3.left, distance, candidate, ref bestLeft, ref bestLeftScore);
        }

        AddIfNotNullAndUnique(result, bestForward);
        AddIfNotNullAndUnique(result, bestBack);
        AddIfNotNullAndUnique(result, bestRight);
        AddIfNotNullAndUnique(result, bestLeft);

        return result;
    }

    private bool IsCandidateValid(DummyPoleNode origin, DummyPoleNode candidate)
    {
        if (candidate == null || candidate == origin)
        {
            return false;
        }

        if (!candidate.includeInDummyNetwork)
        {
            return false;
        }

        Vector3 toCandidate = candidate.Position - origin.Position;
        toCandidate.y = 0f;

        float distance = toCandidate.magnitude;

        if (distance <= 0.001f)
        {
            return false;
        }

        if (distance > maxConnectDistance)
        {
            return false;
        }

        if (minConnectDistance > 0f && distance < minConnectDistance)
        {
            return false;
        }

        if (!allowDiagonalConnections && !IsAxisAlignedEnough(toCandidate.normalized))
        {
            return false;
        }

        return true;
    }

    private bool IsAxisAlignedEnough(Vector3 direction)
    {
        float xAmount = Mathf.Abs(Vector3.Dot(direction, Vector3.right));
        float zAmount = Mathf.Abs(Vector3.Dot(direction, Vector3.forward));

        return Mathf.Max(xAmount, zAmount) >= axisStrictness;
    }

    private void TrySetBestCandidate(
        Vector3 candidateDirection,
        Vector3 baseDirection,
        float distance,
        DummyPoleNode candidate,
        ref DummyPoleNode bestNode,
        ref float bestScore
    )
    {
        float dot = Vector3.Dot(candidateDirection, baseDirection);

        if (dot < directionDotThreshold)
        {
            return;
        }

        float score = dot * 10f - distance * 0.1f;

        if (score > bestScore)
        {
            bestScore = score;
            bestNode = candidate;
        }
    }

    private void AddIfNotNullAndUnique(List<DummyPoleNode> list, DummyPoleNode node)
    {
        if (node == null || list.Contains(node))
        {
            return;
        }

        list.Add(node);
    }

    private bool IsPairAlreadyCreated(DummyPoleNode a, DummyPoleNode b)
    {
        foreach (DummyPair pair in createdPairs)
        {
            if (pair == null)
            {
                continue;
            }

            if ((pair.nodeA == a && pair.nodeB == b) || (pair.nodeA == b && pair.nodeB == a))
            {
                return true;
            }
        }

        return false;
    }

    private bool DoesNewConnectionCrossExisting(DummyPoleNode newA, DummyPoleNode newB)
    {
        Vector2 a = ToXZ(newA.Position);
        Vector2 b = ToXZ(newB.Position);

        foreach (DummyPair pair in createdPairs)
        {
            if (pair == null || pair.nodeA == null || pair.nodeB == null)
            {
                continue;
            }

            // 끝점을 공유하는 선끼리는 교차로 보지 않는다
            if (pair.nodeA == newA || pair.nodeA == newB || pair.nodeB == newA || pair.nodeB == newB)
            {
                continue;
            }

            if (DoLineSegmentsIntersect(a, b, ToXZ(pair.nodeA.Position), ToXZ(pair.nodeB.Position)))
            {
                return true;
            }
        }

        return false;
    }

    private Vector2 ToXZ(Vector3 position)
    {
        return new Vector2(position.x, position.z);
    }

    private bool DoLineSegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float direction1 = Cross(d - c, a - c);
        float direction2 = Cross(d - c, b - c);
        float direction3 = Cross(b - a, c - a);
        float direction4 = Cross(b - a, d - a);

        if (((direction1 > 0f && direction2 < 0f) || (direction1 < 0f && direction2 > 0f)) &&
            ((direction3 > 0f && direction4 < 0f) || (direction3 < 0f && direction4 > 0f)))
        {
            return true;
        }

        return false;
    }

    private float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private void DestroyObject(GameObject target)
    {
        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private class DummyPair
    {
        public DummyPoleNode nodeA;
        public DummyPoleNode nodeB;

        public DummyPair(DummyPoleNode nodeA, DummyPoleNode nodeB)
        {
            this.nodeA = nodeA;
            this.nodeB = nodeB;
        }
    }
}
