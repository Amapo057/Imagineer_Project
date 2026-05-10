using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class CameraMovement : MonoBehaviour
{
    public CinemachineCamera virtualCamera; //Cinemachine Camera 컴포넌트 참조용 객체

    private float zoomSpeed = 7f;  // 줌 속도
    private float minZoomDistance = 4f;  // 최소 줌 거리
    private float maxZoomDistance = 30f;  // 최대 줌 거리

    private CinemachineFollow followComponent;  //Cinemachine Follow 컴포넌트 참조용 객체
    private Vector3 zoomDirection;  // 줌 방향 벡터
    private float currentZoomDistance;  // 현재 줌 거리
    private float scrollInput;  // 마우스 스크롤 입력값

    // 인풋 시스템에서 스크롤 입력을 받아 처리하는 메소드
    void OnScroll(InputValue value)
    {
        scrollInput = value.Get<float>(); // 마우스 스크롤 입력값 가져오기
    }
    // Cinemachine의 초기화에 시간이 걸릴 수 있으므로 Awkake 대신 Start에서 초기화 작업 수행
    void Start()
    {
        followComponent = virtualCamera.GetComponent<CinemachineFollow>(); // virtualCamera에서 Cinemachine Follow 컴포넌트 가져오기
        zoomDirection = followComponent.FollowOffset.normalized; // FollowOffset의 좌표를 받아 정규화해 방향 계산
        currentZoomDistance = followComponent.FollowOffset.magnitude; // magnitude로 목표와 카메라 사이의 거리를 계산하여 초기 줌 거리 설정
    }
    void Update()
    {
        if (scrollInput != 0)
        {
            // 매개변수가 양수면 1, 음수면 -1이 되는 Sign 함수를 사용하여 스크롤 방향에 따라 줌 인/아웃 결정
            // 스크롤은 입력값이 돌리는 강도에 따라다르게 들어오기에 값을 정제하기 위해 sign 함수 사용
            // 휠을 내리는 음수 입력이 카메라의 거리가 증가하는 방향이므로 -를 곱해 부호를 반전시킴
            float directionSign = -Mathf.Sign(scrollInput);
            currentZoomDistance += directionSign * zoomSpeed;  // 방향에 속도를 곱해 이동할 거리를 계산해 현재 위치에 더함
            currentZoomDistance = Mathf.Clamp(currentZoomDistance, minZoomDistance, maxZoomDistance); // 줌 거리를 최소/최대 범위로 제한
        }
        Vector3 targetOffset = zoomDirection * currentZoomDistance;  // 방향벡터에 거리를 곱해 이동할 좌표 계산

        // 부드러운 줌을 위해 Lerp 사용
        // Vector3.Lerp(현재 오프셋, 목표 오프셋, Time.deltaTime * 10f)로 현재 오프셋에서 목표 오프셋까지 부드럽게 이동
        // 마지막 매개변수로 프레임당 변할 비율 조정
        followComponent.FollowOffset = Vector3.Lerp(followComponent.FollowOffset, targetOffset, Time.deltaTime * 10f);
    }
}
