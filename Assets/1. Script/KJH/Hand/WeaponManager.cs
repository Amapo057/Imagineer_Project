using Oculus.Interaction.Input;
using UnityEngine;

public class WeaponManager : MonoBehaviour
{
    [SerializeField] private Hand hand;
    [SerializeField] private Transform weaponAnchor;
    [SerializeField] private GameObject weaponModel;
    bool held;
    public bool TryGetStrikePosition(out Vector3 position)
    {
        position = default;
        if (!held || hand == null || !hand.GetJointPose(HandJointId.HandPalm, out Pose palm)) return false;
        position = palm.position; return true;
    }

    private void Update()
    {
        if (hand == null) return;
        // 인터렉티브  sdk를 사용하니 해당 sdk의 GetJointPose함수를 사용해 손 정보 받기
        if(hand.GetJointPose(HandJointId.HandPalm, out Pose palmPose))
        {
            weaponAnchor.position = palmPose.position;
            weaponAnchor.rotation = palmPose.rotation;
        } 
        var game = ProductionGameController.Current;
        if (game != null && weaponModel != null)
            weaponModel.SetActive(held && game.CanAct && game.PendingWeapons > 0 && TryGetStrikePosition(out _));
    }

    public void RockDetected()
    {
        // 주먹쥐면 무기용 모델 활성화
        held = true;
        var game = ProductionGameController.Current;
        weaponModel.SetActive(game == null || (game.CanAct && game.PendingWeapons > 0));
    }
    public void RockReleased()
    {
        held = false; weaponModel.SetActive(false);
    }
}
