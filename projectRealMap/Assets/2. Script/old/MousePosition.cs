using UnityEngine;
using UnityEngine.InputSystem;

public class MousePosition : MonoBehaviour
{
    // 마우스의 위치를 따라 회전하는 함수
    public Vector3 GetMousePosition(Camera cam)
    {
        // 마우스 위치에서 레이를 생성
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        // 레이와 교차해 값을 계산하기 위한 평면 생성
        // Vector3.up으로 면위 위로 가도록 설정
        // transform.position.y로 플레이어의 높이에 맞춰 평면 위치 설정
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0, transform.position.y, 0));

        // 레이 거리 저장용 변수
        float rayDistance;

        // 레이와 평면이 교차하는지 확인하고, 교차한다면 rayDistance에 교차 지점까지의 거리를 저장
        if (groundPlane.Raycast(ray, out rayDistance))
        {
            // 거리까지의 좌표 계산
            Vector3 mousePoint = ray.GetPoint(rayDistance);
            // y축은 플레이어의 높이를 유지하며 바라볼 방향 리턴
            return new Vector3(mousePoint.x, transform.position.y, mousePoint.z);
        }
        // 레이와 평면이 교차하지 않는 경우, 플레이어가 바라보는 방향을 리턴해 오류 방지
        return transform.position + transform.forward;
    }
}
