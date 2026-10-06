using UnityEngine;

public enum CardSide
{
    Front,
    Back
}

public class MarkerPositionResult
{
    public int markerId;
    public int cardId;
    public CardSide side;

    public Vector3 worldPosition;
    public Quaternion worldRotation;
}
