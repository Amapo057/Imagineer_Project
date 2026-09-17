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

    void Update()
    {
        if (thumbTip == null)
        {
            FindThumbTip();
            return;
        }

        // 현재 엄지 위치값 받아오기
        Vector3 currentThumbPosition = thumbTip.position;

        // 이전 엄지 위치값 맨처음 초기화
        if (!thumbInitialized)
        {
            previousThumbPosition = currentThumbPosition;
            thumbInitialized = true;
            return;
        }

        // 엄지 속도 계산
        // deltaTime사용해 프레임 상관없이 작동하도록 구성
        // 대략 m/s단위
        float measuredSpeed = Vector3.Distance(previousThumbPosition, currentThumbPosition) / Time.deltaTime;
        thumbSpeed = Mathf.Lerp(thumbSpeed, measuredSpeed, 0.2f);
        debug4.text = $"Thumb Speed: {thumbSpeed}";

        // 이전 위치 적용
        previousThumbPosition = currentThumbPosition;
    }

    // 엄지끝 탐색 후 전역변수에 할당
    private void FindThumbTip()
    {
        if(handSkeleton == null) return;
        // 멀쩡한지 확인
        if(!handSkeleton.IsInitialized) return;

        foreach(var bone in handSkeleton.Bones)
        {
            if(bone.Id == OVRSkeleton.BoneId.Hand_ThumbTip)
            {
                thumbTip = bone.Transform;
                break;
            }
        }
    }

    public float GetThumbSpeed()
    {
        return thumbSpeed;
    }
}
