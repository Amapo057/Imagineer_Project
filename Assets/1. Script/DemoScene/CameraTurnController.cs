using System.Collections;
using UnityEngine;

/// <summary>
/// 데모용 카메라 회전 컨트롤러 — 턴이 넘어갈 때 테이블 중심(pivot)을 기준으로 카메라를 180도 돌려서
/// "반대편에서 보는 느낌"을 냄. 실제 MR/마커 트래킹용 카메라가 아니라 에디터 테스트(Main Camera) 전용 연출.
/// </summary>
public class CameraTurnController : MonoBehaviour
{
    [Tooltip("회전 기준점. 비워두면 월드 원점(0,0,0) 기준으로 돎")]
    [SerializeField] private Transform pivot;
    [SerializeField] private float turnDuration = 0.6f;

    private Coroutine turnRoutine;

    // 턴이 넘어갈 때마다 호출 — pivot 기준으로 카메라를 180도 회전시킴 (Y축 기준)
    public void FlipCamera()
    {
        if (turnRoutine != null) StopCoroutine(turnRoutine);
        turnRoutine = StartCoroutine(RotateAroundPivot(180f, turnDuration));
    }

    // 게임 시작 직후, 실제 선공이 Opponent로 랜덤 결정됐을 때 한 번만 호출해서 씀.
    // Main Camera의 기본 배치는 항상 "Me가 선공"이라고 가정한 방향이라, Opponent가 선공이면
    // 1턴부터 이미 카메라가 반대로 시작해서 그 뒤로 End Turn을 누를 때마다 계속 한 턴씩 밀린 채로
    // 뒤집히는 문제가 있었음 — 애니메이션 없이 즉시 180도 돌려서 실제 선공 쪽에 카메라를 맞춰둠
    public void SnapFlip()
    {
        if (turnRoutine != null) StopCoroutine(turnRoutine);

        Vector3 pivotPoint = pivot != null ? pivot.position : Vector3.zero;
        transform.RotateAround(pivotPoint, Vector3.up, 180f);
    }

    private IEnumerator RotateAroundPivot(float angle, float duration)
    {
        Vector3 pivotPoint = pivot != null ? pivot.position : Vector3.zero;
        float rotated = 0f;

        while (rotated < angle)
        {
            float step = Mathf.Min((angle / duration) * Time.deltaTime, angle - rotated);
            transform.RotateAround(pivotPoint, Vector3.up, step);
            rotated += step;
            yield return null;
        }
    }
}
