using Oculus.Interaction.Input;
using UnityEngine;

public class WeaponManager : MonoBehaviour
{
    [SerializeField] private Hand hand;
    [SerializeField] private Transform weaponAnchor;
    [SerializeField] private GameObject weaponModel;

    private void Update()
    {
        if (hand == null) return;
        // 인터렉티브  sdk를 사용하니 해당 sdk의 GetJointPose함수를 사용해 손 정보 받기
        if(hand.GetJointPose(HandJointId.HandPalm, out Pose palmPose))
        {
            weaponAnchor.position = palmPose.position;
            weaponAnchor.rotation = palmPose.rotation;
        } 
    }

    public void RockDetected()
    {
        // 주먹쥐면 무기용 모델 활성화
        weaponModel.SetActive(true);
    }
    public void RockReleased()
    {
        weaponModel.SetActive(false);
    }
}
