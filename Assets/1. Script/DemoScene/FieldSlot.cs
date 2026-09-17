using UnityEngine;

/// <summary>
/// 필드 라인 한 칸을 나타내는 표식. MyFieldPosition/EnemyFieldPosition 하위의 각 필드 오브젝트(FieldPosition,
/// FieldPosition (1) 등)에 하나씩 붙여서 "몇 번 라인인지" + "어느 편 필드인지"를 갖고 있게 함.
/// CardMove가 카드를 여기 내려놓을 때 이 정보로 실제 GameBoardState.lanes에 하수인을 등록함.
/// </summary>
public class FieldSlot : MonoBehaviour
{
    [Tooltip("이 슬롯의 라인 인덱스 (0~3)")]
    [SerializeField] private int laneIndex;

    [Tooltip("이 슬롯이 내 필드인지 상대 필드인지")]
    [SerializeField] private PlayerSide side;

    public int LaneIndex => laneIndex;
    public PlayerSide Side => side;

    // 지금 이 슬롯에 놓여있는 카드의 비주얼 오브젝트 (없으면 null).
    // 하수인이 전투로 죽으면 DemoTurnController가 이 참조로 비주얼 오브젝트를 같이 파괴함
    [System.NonSerialized] public GameObject occupyingCard;
}
