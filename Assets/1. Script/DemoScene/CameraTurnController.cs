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
