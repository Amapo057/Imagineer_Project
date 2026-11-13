using UnityEngine;

// Keep canonical lane X coordinates identical on both peers; only swap the local half.
public sealed class ProductionSeating : MonoBehaviour
{
    public Transform field;
    public Camera head;
    public AnchorManager anchor;
    public MarkerWorldPos marker;
    public Transform[] localObjects;
    public Transform[] remoteObjects;
    public bool IsReady { get; private set; }
    public bool PositiveSide { get; private set; }
    public static Vector3 SeatPosition(Vector3 position, bool positive) => positive ? new Vector3(position.x, position.y, -position.z) : position;
    void Update()
    {
        if (IsReady || !anchor.IsReady || head == null) return;
        float z = field.InverseTransformPoint(head.transform.position).z;
        if (Mathf.Abs(z) < 0.05f) return;
        PositiveSide = z > 0;
        foreach (var item in localObjects) Place(item);
        foreach (var item in remoteObjects) Place(item);
        marker.LocalPlayerOnPositiveSide = PositiveSide;
        IsReady = true;
    }
    void Place(Transform item)
    {
        if (!PositiveSide || item == null) return;
        item.position = field.TransformPoint(SeatPosition(field.InverseTransformPoint(item.position), true));
        item.rotation = Quaternion.AngleAxis(180, field.up) * item.rotation;
    }
}
