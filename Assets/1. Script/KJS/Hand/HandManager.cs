using UnityEngine;

public class HandManager : MonoBehaviour
{
    [SerializeField] private OVRSkeleton handSkeleton;

    private Transform thumbTip;
    private Transform wrist;
    private Vector3 previousThumbPosition;
    private Vector3 previousWristPosition;
    private bool hasPreviousSample;
    private float thumbSpeed;
    private float wristSpeed;
    private const float SmoothingSpeed = 23f;

    private void Update()
    {
        if (handSkeleton == null || !handSkeleton.IsInitialized || !handSkeleton.IsDataValid)
        {
            hasPreviousSample = false;
            thumbSpeed = wristSpeed = 0f;
            return;
        }
        if ((thumbTip == null || wrist == null) && !BindHandJoints()) return;
        SampleHandSpeed(Time.deltaTime);
    }

    private bool BindHandJoints()
    {
        var skeletonType = handSkeleton.GetSkeletonType();
        bool openXR = skeletonType == OVRSkeleton.SkeletonType.XRHandLeft ||
                      skeletonType == OVRSkeleton.SkeletonType.XRHandRight;
        var thumbId = openXR ? OVRSkeleton.BoneId.XRHand_ThumbTip : OVRSkeleton.BoneId.Hand_ThumbTip;
        var wristId = openXR ? OVRSkeleton.BoneId.XRHand_Wrist : OVRSkeleton.BoneId.Hand_WristRoot;
        foreach (var bone in handSkeleton.Bones)
        {
            if (bone.Id == thumbId) thumbTip = bone.Transform;
            if (bone.Id == wristId) wrist = bone.Transform;
        }
        return thumbTip != null && wrist != null;
    }

    private void SampleHandSpeed(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        Vector3 thumbPosition = thumbTip.position;
        Vector3 wristPosition = wrist.position;
        if (hasPreviousSample)
        {
            float t = 1f - Mathf.Exp(-SmoothingSpeed * deltaTime);
            thumbSpeed = Mathf.Lerp(thumbSpeed, Vector3.Distance(previousThumbPosition, thumbPosition) / deltaTime, t);
            wristSpeed = Mathf.Lerp(wristSpeed, Vector3.Distance(previousWristPosition, wristPosition) / deltaTime, t);
        }
        previousThumbPosition = thumbPosition;
        previousWristPosition = wristPosition;
        hasPreviousSample = true;
    }

    public float GetHandSpeed() => Mathf.Max(thumbSpeed, wristSpeed);
}
