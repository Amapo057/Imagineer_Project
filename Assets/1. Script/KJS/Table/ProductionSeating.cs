using UnityEngine;

// Swap sides across the table while keeping card lanes aligned between players.
public sealed class ProductionSeating : MonoBehaviour
{
    public Transform field;
    public Camera head;
    public AnchorManager anchor;
    public MarkerWorldPos marker;
    public Transform[] localObjects;
    public Transform[] remoteObjects;
    [SerializeField] private Transform localDeck;
    [SerializeField] private Transform opponentDeck;
    [SerializeField] private Transform localMana;
    [SerializeField] private Transform opponentMana;
    public bool IsReady { get; private set; }
    public bool PositiveSide { get; private set; }
    public static Vector3 SeatPosition(Vector3 position, bool positive) => positive ? new Vector3(position.x, position.y, -position.z) : position;
    void Update()
    {
        if (IsReady || !anchor.IsReady || head == null) return;
        float z = field.InverseTransformPoint(head.transform.position).z;
        if (Mathf.Abs(z) < 0.05f) return;
        PositiveSide = z > 0;
        if (PositiveSide)
        {
            foreach (var item in localObjects) PlaceOnOppositeSide(item);
            foreach (var item in remoteObjects) PlaceOnOppositeSide(item);
        }
        marker.LocalPlayerOnPositiveSide = PositiveSide;
        IsReady = true;
    }
    void PlaceOnOppositeSide(Transform item)
    {
        if (item == null) return;
        var position = SeatPosition(field.InverseTransformPoint(item.position), true);
        if (item == localDeck || item == opponentDeck || item == localMana || item == opponentMana)
            position.x = -position.x;
        item.position = field.TransformPoint(position);
        item.rotation = Quaternion.AngleAxis(180, field.up) * item.rotation;
    }
}
