using System;
using TMPro;
using UnityEngine;

public class HandManager : MonoBehaviour
{
    [SerializeField] private OVRSkeleton handSkeleton;
    [SerializeField] private TMPro.TextMeshProUGUI debug4;

    // 엄지끝 정보용 변수
    private Transform thumbTip = null;
    // 속도 측정용 이전 엄지 위치
    private Vector3 previousThumbPosition;
    // 엄지 이전 위치값 초기화 여부
    private bool thumbInitialized = false;
    // 엄지 속도
    private float thumbSpeed;

    // 손목용 변수
    private Transform wrist = null;
    private Vector3 previousWristPosition;
    private bool wristInitialized = false;
    private float wristSpeed;

    // 보간용 변수
    // 약 0.1초에 90% 반영하도록 설정
    float smoothingSpeed = 23f;

    void Update()
    {
        if (thumbTip == null)
        {
            FindThumbTip();
            return;
        }
        if (wrist == null)
        {
            Findwrist();
            return;
        }

        // 현재 엄지 위치값 받아오기
        Vector3 currentThumbPosition = thumbTip.position;
        Vector3 currentWristPosition = wrist.position;

        // 이전 엄지 위치값 맨처음 초기화
        if (!thumbInitialized)
        {
            previousThumbPosition = currentThumbPosition;
            thumbInitialized = true;
            return;
        }
        if (!wristInitialized)
        {
            previousWristPosition = currentWristPosition;
            wristInitialized = true;
            return;
        }

        // 1 - e^(-23 * 0.1) = 약 0.9
        float t = 1f - Mathf.Exp(-smoothingSpeed * Time.deltaTime);

        // 엄지 속도 계산
        // deltaTime사용해 프레임 상관없이 작동하도록 구성
        // 대략 m/s단위
        float thumbMeasuredSpeed = Vector3.Distance(previousThumbPosition, currentThumbPosition) / Time.deltaTime;
        thumbSpeed = Mathf.Lerp(thumbSpeed, thumbMeasuredSpeed, t);

        float wristMeasuredSpeed = Vector3.Distance(previousWristPosition, currentWristPosition) / Time.deltaTime;
        wristSpeed = Mathf.Lerp(wristSpeed, wristMeasuredSpeed, t);

        debug4.text = $"Thumb Speed: {thumbSpeed}\nWrist Speed: {wristSpeed}";

        // 이전 위치 적용
        previousThumbPosition = currentThumbPosition;
        previousWristPosition = currentWristPosition;
    }

    // 엄지끝 탐색 후 전역변수에 할당
    private void FindThumbTip()
    {
        // 멀쩡한지 확인
        if(handSkeleton == null || !handSkeleton.IsInitialized) return;

        // 뼈 내부 탐색 후 찾으면 위치 할당
        foreach(var bone in handSkeleton.Bones)
        {
            if(bone.Id == OVRSkeleton.BoneId.Hand_ThumbTip)
            {
                thumbTip = bone.Transform;
                break;
            }
        }
    }

    private void Findwrist()
    {
        // 멀쩡한지 확인
        if(handSkeleton == null || !handSkeleton.IsInitialized) return;

        // 뼈 내부 탐색 후 찾으면 위치 할당
        foreach(var bone in handSkeleton.Bones)
        {
            if(bone.Id == OVRSkeleton.BoneId.Hand_WristRoot)
            {
                wrist = bone.Transform;
                break;
            }
        }
    }

    public float GetHandSpeed()
    {
        // 둘중 더 큰것 반환
        return  Mathf.Max(thumbSpeed, wristSpeed);
    }
}
