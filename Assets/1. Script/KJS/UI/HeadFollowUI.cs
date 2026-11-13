using UnityEngine;

public class HeadFollowUI : MonoBehaviour
{
    [SerializeField] private Transform head;
    [SerializeField, Range(0f, 1f)] private float positionFollow = 0.9f;
    [SerializeField, Range(0f, 1f)] private float rotationFollow = 0.7f;

    private Vector3 positionOffset;
    private Quaternion rotationOffset;

    private void Start()
    {
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (head == null)
        {
            Debug.LogWarning("HeadFollowUI: 머리 카메라가 연결되지 않았습니다.", this);
            enabled = false;
            return;
        }

        positionOffset = head.InverseTransformPoint(transform.position);
        rotationOffset = Quaternion.Inverse(head.rotation) * transform.rotation;
    }

    private void LateUpdate()
    {
        float positionT = 1f - Mathf.Pow(1f - positionFollow, Time.unscaledDeltaTime);
        float rotationT = 1f - Mathf.Pow(1f - rotationFollow, Time.unscaledDeltaTime);
        transform.position = Vector3.Lerp(transform.position, head.TransformPoint(positionOffset), positionT);
        transform.rotation = Quaternion.Slerp(transform.rotation, head.rotation * rotationOffset, rotationT);
    }
}
