using UnityEngine;

public class AnchorManager : MonoBehaviour
{
    [SerializeField] private GameObject fieldRoot;
    [SerializeField] private VisualOffset visualOffset;
    private OVRSpatialAnchor spatialAnchor;

    // 테이블 테스트하기 편하게 약간 앞으로 밈
    private Vector3 platePosition = new Vector3(0f, 0f, 0.2f);
    private Vector3 plateRotation = new Vector3(180f, -90f, -90f);

    public void CreateTableAnchor(Vector3 anchorPosition, Quaternion anchorRotation)
    {
        // 앵커용 빈 오브젝트 생성
        GameObject anchorObject = new GameObject("FieldAnchor");
        // 위치 적용
        anchorObject.transform.SetPositionAndRotation(anchorPosition, anchorRotation);

        // 앵거 배정
        spatialAnchor = anchorObject.AddComponent<OVRSpatialAnchor>();

        // 필드 루트를 앵커의 자식의로 넣어 따라다니도록 설정
        fieldRoot.transform.SetParent(anchorObject.transform);
        // 앵커의 자식으로 들어간 후 앵커 기준의 원점으로 위치 초기화
        fieldRoot.transform.localPosition = Vector3.zero;
        fieldRoot.transform.localRotation = Quaternion.identity;
        visualOffset.OffsetAdjustment(platePosition, plateRotation);
    }
}
