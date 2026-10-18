using UnityEngine;
using Oculus.Interaction.Input;

public class HandMask : MonoBehaviour
{
    [SerializeField] private Hand lefthand;
    [SerializeField] private Hand righthand;
    [SerializeField] private Transform centerEye;

    private float maskRadius = 0.075f;

    private void LateUpdate()
    {
        // 눈 없을시 리턴
        if (centerEye == null)
        {
            Shader.SetGlobalFloat("_HandMaskradius", 0f);
            return;
        }
        Shader.SetGlobalVector("_CenterEyeWorldPos", centerEye.position);
        Shader.SetGlobalFloat("_HandMaskRadius", maskRadius);

        UpdateHand(lefthand, "_LeftHandWorldPos");
        UpdateHand(righthand, "_RightHandWorldPos");
            
    }

    private static void UpdateHand(Hand hand, string property)
    {
        Vector4 position = Vector4.zero;

        // 손 있는지 확인 후 있으면 좌표 연결
        if(hand != null && hand.GetJointPose(HandJointId.HandMiddle2, out Pose pose))
        {
            position = pose.position;
            // 손 추적 여부용 4번째 값
            position.w = 1f;
        }
        // 인자로 받은 쉐이더 변수 변경
        Shader.SetGlobalVector(property, position);
    }
}
