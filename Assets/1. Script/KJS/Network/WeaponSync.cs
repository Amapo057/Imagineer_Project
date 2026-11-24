using UnityEngine;

// Read the local weapon after WeaponAnchor has updated its visibility and pose.
[DefaultExecutionOrder(100)]
public sealed class WeaponSync : MonoBehaviour
{
    [SerializeField] private ProductionGameController session;
    [SerializeField] private WeaponManager weaponManager;
    [SerializeField, Range(5f, 30f)] private float sendRate = 20f;
    [SerializeField, Min(0.1f)] private float receiveTimeout = 0.4f;

    private GameNetworkManager network;
    private GameManager match;
    private Transform field;
    private Transform remoteAnchor;
    private GameObject remoteWeapon;
    private int remoteCardId;
    private int remoteTurn;
    private Pose remotePose;
    private float lastReceiveTime;
    private int sentCardId;
    private int sentTurn;
    private float nextSendTime;

    void OnEnable()
    {
        field = session.seating.field;
        network = session.Network;
        network.OnWeaponPoseReceived += ReceivePose;
        network.OnDisconnected += ResetVisuals;
    }

    void OnDisable()
    {
        if (network != null)
        {
            if (network.Connected && sentCardId != 0) network.SendWeaponPose(sentTurn, 0, default);
            network.OnWeaponPoseReceived -= ReceivePose;
            network.OnDisconnected -= ResetVisuals;
        }
        ResetVisuals();
    }

    void OnDestroy()
    {
        if (remoteAnchor != null) Destroy(remoteAnchor.gameObject);
    }

    void LateUpdate()
    {
        BindMatch();
        float now = Time.unscaledTime;
        bool canSync = CanSync();
        PublishPose(canSync, now);

        if (remoteWeapon == null || !remoteWeapon.activeSelf) return;
        if (!canSync || match.State.CurrentPlayer != PlayerSide.Opponent ||
            remoteTurn != match.TurnNumber || remoteCardId != session.OpponentWeaponCardId ||
            now - lastReceiveTime >= receiveTimeout)
        {
            HideRemote();
            return;
        }

        float blend = 1f - Mathf.Exp(-28f * Time.unscaledDeltaTime);
        remoteAnchor.SetLocalPositionAndRotation(
            Vector3.Lerp(remoteAnchor.localPosition, remotePose.position, blend),
            Quaternion.Slerp(remoteAnchor.localRotation, remotePose.rotation, blend));
    }

    private void PublishPose(bool canSync, float now)
    {
        if (canSync && session.CanAct && weaponManager.TryGetVisibleWeapon(out int source, out var pose))
        {
            int turn = match.TurnNumber;
            if (sentCardId == source && sentTurn == turn && now < nextSendTime) return;
            var fieldPose = new Pose(field.InverseTransformPoint(pose.position),
                Quaternion.Inverse(field.rotation) * pose.rotation);
            if (network.SendWeaponPose(turn, source, fieldPose))
            {
                sentCardId = source;
                sentTurn = turn;
                nextSendTime = now + 1f / sendRate;
            }
        }
        else if (sentCardId != 0)
        {
            if (network.Connected) network.SendWeaponPose(sentTurn, 0, default);
            sentCardId = 0;
        }
    }

    private void ReceivePose(int turn, int cardId, Pose pose)
    {
        BindMatch();
        if (turn != match.TurnNumber) return;
        if (cardId == 0)
        {
            HideRemote();
            return;
        }
        if (!CanSync() || match.State.CurrentPlayer != PlayerSide.Opponent ||
            cardId != session.OpponentWeaponCardId) return;

        if (remoteWeapon == null || remoteCardId != cardId)
        {
            var prefab = weaponManager.GetWeaponPrefab(cardId);
            if (prefab == null) return;
            if (remoteAnchor == null)
            {
                remoteAnchor = new GameObject("Opponent Weapon Visual").transform;
                remoteAnchor.SetParent(field, false);
            }
            HideRemote();
            if (remoteWeapon != null) Destroy(remoteWeapon);
            remoteAnchor.SetLocalPositionAndRotation(pose.position, pose.rotation);
            remoteWeapon = Instantiate(prefab, remoteAnchor, false);
            foreach (var collider in remoteWeapon.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in remoteWeapon.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            remoteCardId = cardId;
        }

        if (!remoteWeapon.activeSelf)
        {
            remoteAnchor.SetLocalPositionAndRotation(pose.position, pose.rotation);
            remoteWeapon.SetActive(true);
        }
        remotePose = pose;
        remoteTurn = turn;
        lastReceiveTime = Time.unscaledTime;
    }

    private bool CanSync() => session.isActiveAndEnabled && session.Ready && session.HardwareReady &&
        network.Connected && network.PeerGameplayReady && !match.IsGameOver && !session.CombatPlaying;

    private void BindMatch()
    {
        var game = session.Game;
        if (match == game) return;
        match = game;
        ResetVisuals();
    }

    private void ResetVisuals()
    {
        sentCardId = 0;
        HideRemote();
    }

    private void HideRemote()
    {
        if (remoteWeapon != null && remoteWeapon.activeSelf) remoteWeapon.SetActive(false);
    }
}
