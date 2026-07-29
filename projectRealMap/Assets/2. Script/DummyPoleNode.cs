using UnityEngine;

// 이동에 전혀 관여하지 않는 장식 전용 전봇대
//
// PoleNode를 상속하지 않는 것이 핵심이다.
// 상속하면 PoleNetworkBuilder의 FindObjectsByType<PoleNode>()가 자식 클래스까지 전부 잡아가서
// 이동용 네트워크에 섞여버린다. 완전히 별개 타입으로 두면 구조적으로 섞일 수 없다.
public class DummyPoleNode : MonoBehaviour
{
    [Header("전선이 걸리는 위치")]
    [Tooltip("비워두면 이 오브젝트의 위치를 사용합니다.")]
    public Transform wirePoint;

    [Header("연결 사용 여부")]
    [Tooltip("끄면 이 전봇대는 어떤 장식 전선도 만들지 않습니다.")]
    public bool includeInDummyNetwork = true;

    // 전선이 실제로 걸릴 Transform
    public Transform WireTransform
    {
        get
        {
            if (wirePoint != null)
            {
                return wirePoint;
            }

            return transform;
        }
    }

    public Vector3 Position
    {
        get
        {
            return WireTransform.position;
        }
    }

    private void Reset()
    {
        wirePoint = transform;
    }

    private void OnDrawGizmos()
    {
        // 이동용 PoleNode는 노란색이므로 구분되도록 다른 색을 쓴다
        Gizmos.color = includeInDummyNetwork
            ? new Color(1f, 0.35f, 0.85f, 1f)
            : new Color(0.4f, 0.4f, 0.4f, 1f);

        Gizmos.DrawSphere(Position, 0.25f);
    }
}
