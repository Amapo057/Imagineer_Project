using UnityEngine;
// 인풋시스템 사용
using UnityEngine.InputSystem;

// rigidbody 컴포넌트 강제
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    public float speed = 10f; // 이동 속도
    private Vector2 moveInput; // 이동 입력값

    private Rigidbody rb; // Rigidbody 컴포넌트 참조용 객체
    private Camera mainCamera; // 메인 카메라 참조용 객체
    private MousePosition mousePosition; // 마우스 위치 계산 스크립트 참조용 객체

    // 인풋 시스템에서 이동 입력을 처리하는 메서드
    void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>(); // 입력값을 Vector2로 가져오기
    }

    // 우선적으로 필요한 컴포넌트 가져오기위해 가장 먼저 실행되는 Awake 사용
    void Awake()
    {
        rb = GetComponent<Rigidbody>(); // Rigidbody 컴포넌트 가져오기
        // Rigidbody의 x축과 z축 회전 고정. 인스펙터에서 켜두면 사용 안해도 됨
        // rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        mainCamera = Camera.main; // Main Camera 태그를 가진 카메라 연결

        mousePosition = GetComponent<MousePosition>(); // MousePosition 스크립트 가져오기

    }

    // 물리연산이 아닌것은 update에서 처리
    private void Update()
    {
        if (mainCamera != null && mousePosition != null)
        {
            Vector3 targetPosition = mousePosition.GetMousePosition(mainCamera); // 마우스 위치 계산
            transform.LookAt(targetPosition); // 플레이어가 마우스 위치를 바라보도록 회전
        }
    }

    // 물리연산을 위해 FixedUpdate 사용
    void FixedUpdate()
    {
        // OnMove 메서드에서 받은 입력값을 기반으로 이동 방향과 목표 속도 계산
        Vector3 moveDirection = new Vector3(moveInput.x, 0, moveInput.y); // 이동 방향 계산
        Vector3 targetVelocity = moveDirection * speed; // veclocity는 그 자체로 초당 이동할 거리이므로 deltaTime을 곱할 필요 없음
        targetVelocity.y = rb.linearVelocity.y; // 현재 y속도를 유지하기 위해 targetVelocity의 y값을 현재 rb의 linearVelocity의 y값으로 설정
        rb.linearVelocity = targetVelocity; // Rigidbody의 속도를 목표 속도로 설정


    }
}
