using UnityEngine;
using UnityEngine.Splines;

public class ElectricPoleMover : MonoBehaviour
{
    [Header("현재 서 있는 전봇대 노드")]
    [SerializeField] private PoleNode currentNode;

    [Header("이동 설정")]
    [SerializeField] private float moveSpeed = 8f;

    [Header("방향 입력 감도")]
    [Tooltip("입력 방향과 후보 노드 방향이 최소한 이 정도는 비슷해야 이동합니다. 낮을수록 느슨합니다.")]
    [Range(0f, 1f)]
    [SerializeField] private float directionThreshold = 0.2f;

    [Tooltip("축 판정을 얼마나 느슨하게 볼지 정합니다. 높을수록 대각선에 가까운 후보도 허용됩니다.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float axisTolerance = 0.35f;

    [Header("입력 기준")]
    [Tooltip("비워두면 월드 기준 WASD로 이동합니다. 카메라를 넣으면 카메라 기준 WASD가 됩니다.")]
    [SerializeField] private Transform cameraTransform;

    [Header("방향 판정 설정")]
    [Tooltip("켜면 W/S는 앞뒤 후보 위주로, A/D는 좌우 후보 위주로 선택합니다.")]
    [SerializeField] private bool useStrictInputAxis = false;

    [Header("입력 예약 설정")]
    [Tooltip("이동 중 다음 방향 입력을 미리 받아 도착 후 바로 이어서 이동합니다.")]
    [SerializeField] private bool useInputBuffer = true;

    [Tooltip("예약 입력이 유지되는 시간입니다. 너무 짧으면 입력이 씹히고, 너무 길면 원치 않는 이동이 이어질 수 있습니다.")]
    [SerializeField] private float inputBufferTime = 0.5f;

    [Header("디버그")]
    [SerializeField] private bool showDebugLog = true;

    private WireConnection currentConnection;
    private bool isMoving;

    private float splineProgress;
    private float splineMoveDirection;

    private Vector3 currentInputDirection = Vector3.zero;

    private bool hasBufferedInput;
    private Vector3 bufferedInputDirection;
    private InputAxisType bufferedInputAxis;
    private float bufferedInputTimer;

    private enum InputAxisType
    {
        None,
        ForwardBack,
        LeftRight
    }

    private InputAxisType currentInputAxis = InputAxisType.None;

    public PoleNode CurrentNode => currentNode;
    public bool IsMoving => isMoving;

    private void Start()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (currentNode != null)
        {
            transform.position = currentNode.Position;

            if (showDebugLog)
            {
                Debug.Log("[Move Init] 시작 노드: " + currentNode.name);
            }
        }
        else
        {
            if (showDebugLog)
            {
                Debug.LogWarning("[Move Init Failed] Current Node가 비어 있습니다. Player의 ElectricPoleMover에 시작 MovePoint를 넣어주세요.");
            }
        }
    }

    private void Update()
    {
        UpdateBufferedInputTimer();

        Vector3 inputDirection = GetInputDirection();

        if (isMoving)
        {
            if (inputDirection != Vector3.zero)
            {
                SaveBufferedInput(inputDirection);
            }

            MoveAlongSpline();
            return;
        }

        if (inputDirection == Vector3.zero)
        {
            return;
        }

        TryStartMove(inputDirection, currentInputAxis);
    }

    private void UpdateBufferedInputTimer()
    {
        if (!hasBufferedInput)
        {
            return;
        }

        bufferedInputTimer -= Time.deltaTime;

        if (bufferedInputTimer <= 0f)
        {
            ClearBufferedInput();

            if (showDebugLog)
            {
                Debug.Log("[Input Buffer] 예약 입력 시간이 만료되었습니다.");
            }
        }
    }

    private void SaveBufferedInput(Vector3 inputDirection)
    {
        if (!useInputBuffer)
        {
            return;
        }

        hasBufferedInput = true;
        bufferedInputDirection = inputDirection;
        bufferedInputAxis = currentInputAxis;
        bufferedInputTimer = inputBufferTime;

        if (showDebugLog)
        {
            Debug.Log(
                "[Input Buffer Saved] Axis: " +
                bufferedInputAxis +
                " / Direction: " +
                bufferedInputDirection
            );
        }
    }

    private void ClearBufferedInput()
    {
        hasBufferedInput = false;
        bufferedInputDirection = Vector3.zero;
        bufferedInputAxis = InputAxisType.None;
        bufferedInputTimer = 0f;
    }

    private Vector3 GetInputDirection()
    {
        Vector2 input = Vector2.zero;
        currentInputAxis = InputAxisType.None;

        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
        {
            input.y = 1f;
            currentInputAxis = InputAxisType.ForwardBack;
        }
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
        {
            input.y = -1f;
            currentInputAxis = InputAxisType.ForwardBack;
        }
        else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
        {
            input.x = -1f;
            currentInputAxis = InputAxisType.LeftRight;
        }
        else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            input.x = 1f;
            currentInputAxis = InputAxisType.LeftRight;
        }

        if (input == Vector2.zero)
        {
            currentInputDirection = Vector3.zero;
            return Vector3.zero;
        }

        Vector3 result;

        if (cameraTransform != null)
        {
            Vector3 forward = cameraTransform.forward;
            Vector3 right = cameraTransform.right;

            forward.y = 0f;
            right.y = 0f;

            forward.Normalize();
            right.Normalize();

            result = (forward * input.y + right * input.x).normalized;
        }
        else
        {
            result = new Vector3(input.x, 0f, input.y).normalized;
        }

        currentInputDirection = result;

        if (showDebugLog)
        {
            Debug.Log(
                "[Input] Axis: " +
                currentInputAxis +
                " / Direction: " +
                currentInputDirection
            );
        }

        return result;
    }

    private bool TryStartMove(Vector3 inputDirection, InputAxisType inputAxis)
    {
        currentInputDirection = inputDirection;
        currentInputAxis = inputAxis;

        WireConnection nextConnection = FindBestConnection(inputDirection);

        if (nextConnection == null)
        {
            if (showDebugLog)
            {
                Debug.LogWarning("[Move Failed] 입력 방향에 맞는 연결을 찾지 못했습니다.");
            }

            return false;
        }

        if (nextConnection.targetNode == null)
        {
            if (showDebugLog)
            {
                Debug.LogWarning("[Move Failed] 선택된 Connection의 Target Node가 비어 있습니다.");
            }

            return false;
        }

        if (nextConnection.wireSpline == null)
        {
            if (showDebugLog)
            {
                Debug.LogWarning("[Move Failed] 선택된 Connection의 Wire Spline이 비어 있습니다. Target: " + nextConnection.targetNode.name);
            }

            return false;
        }

        StartSplineMove(nextConnection);
        return true;
    }

    private WireConnection FindBestConnection(Vector3 inputDirection)
    {
        if (currentNode == null)
        {
            if (showDebugLog)
            {
                Debug.LogWarning("[Move Failed] Current Node가 비어 있습니다.");
            }

            return null;
        }

        if (currentNode.connections == null)
        {
            if (showDebugLog)
            {
                Debug.LogWarning("[Move Failed] 현재 노드의 Connections가 null입니다. Node: " + currentNode.name);
            }

            return null;
        }

        if (currentNode.connections.Count == 0)
        {
            if (showDebugLog)
            {
                Debug.LogWarning("[Move Failed] 현재 노드의 Connections가 0개입니다. 고립 노드일 가능성이 있습니다. Node: " + currentNode.name);
            }

            return null;
        }

        if (showDebugLog)
        {
            Debug.Log(
                "[Move Search] 현재 노드: " +
                currentNode.name +
                " / 연결 개수: " +
                currentNode.connections.Count
            );
        }

        WireConnection bestConnection = null;
        float bestScore = float.MinValue;

        foreach (WireConnection connection in currentNode.connections)
        {
            if (connection == null)
            {
                if (showDebugLog)
                {
                    Debug.LogWarning("[Candidate Rejected] Connection이 null입니다.");
                }

                continue;
            }

            if (connection.targetNode == null)
            {
                if (showDebugLog)
                {
                    Debug.LogWarning("[Candidate Rejected] Target Node가 비어 있습니다.");
                }

                continue;
            }

            if (connection.wireSpline == null)
            {
                if (showDebugLog)
                {
                    Debug.LogWarning("[Candidate Rejected] Wire Spline이 비어 있습니다. Target: " + connection.targetNode.name);
                }

                continue;
            }

            Vector3 directionToTarget = connection.targetNode.Position - currentNode.Position;
            directionToTarget.y = 0f;

            if (directionToTarget.sqrMagnitude <= 0.001f)
            {
                if (showDebugLog)
                {
                    Debug.LogWarning("[Candidate Rejected] 목적지 방향이 너무 짧습니다. Target: " + connection.targetNode.name);
                }

                continue;
            }

            Vector3 targetDirection = directionToTarget.normalized;

            if (useStrictInputAxis)
            {
                if (!IsTargetAllowedByInputAxis(targetDirection))
                {
                    if (showDebugLog)
                    {
                        Debug.Log(
                            "[Candidate Rejected By Axis] Target: " +
                            connection.targetNode.name +
                            " / Target Direction: " +
                            targetDirection
                        );
                    }

                    continue;
                }
            }

            float dot = Vector3.Dot(inputDirection.normalized, targetDirection);

            if (dot < directionThreshold)
            {
                if (showDebugLog)
                {
                    Debug.Log(
                        "[Candidate Rejected By Dot] Target: " +
                        connection.targetNode.name +
                        " / Dot: " +
                        dot +
                        " / Required: " +
                        directionThreshold
                    );
                }

                continue;
            }

            float distance = directionToTarget.magnitude;
            float score = dot * 10f - distance * 0.1f;

            if (showDebugLog)
            {
                Debug.Log(
                    "[Candidate Accepted] Target: " +
                    connection.targetNode.name +
                    " / Dot: " +
                    dot +
                    " / Distance: " +
                    distance +
                    " / Score: " +
                    score +
                    " / Spline: " +
                    connection.wireSpline.name
                );
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestConnection = connection;
            }
        }

        if (bestConnection != null)
        {
            if (showDebugLog)
            {
                Debug.Log(
                    "[Move Selected] " +
                    currentNode.name +
                    " -> " +
                    bestConnection.targetNode.name
                );
            }
        }
        else
        {
            if (showDebugLog)
            {
                Debug.LogWarning("[Move Failed] 모든 후보가 탈락했습니다. Node: " + currentNode.name);
            }
        }

        return bestConnection;
    }

    private bool IsTargetAllowedByInputAxis(Vector3 targetDirection)
    {
        if (currentInputAxis == InputAxisType.None)
        {
            return true;
        }

        Vector3 forward;
        Vector3 right;

        if (cameraTransform != null)
        {
            forward = cameraTransform.forward;
            right = cameraTransform.right;
        }
        else
        {
            forward = Vector3.forward;
            right = Vector3.right;
        }

        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        float inputDot = Vector3.Dot(targetDirection, currentInputDirection.normalized);

        if (inputDot < directionThreshold)
        {
            return false;
        }

        if (currentInputAxis == InputAxisType.ForwardBack)
        {
            float forwardAmount = Mathf.Abs(Vector3.Dot(targetDirection, forward));
            float rightAmount = Mathf.Abs(Vector3.Dot(targetDirection, right));

            return forwardAmount + axisTolerance >= rightAmount;
        }

        if (currentInputAxis == InputAxisType.LeftRight)
        {
            float rightAmount = Mathf.Abs(Vector3.Dot(targetDirection, right));
            float forwardAmount = Mathf.Abs(Vector3.Dot(targetDirection, forward));

            return rightAmount + axisTolerance >= forwardAmount;
        }

        return true;
    }

    private void StartSplineMove(WireConnection connection)
    {
        currentConnection = connection;
        isMoving = true;

        Vector3 splineStart = GetSplinePosition(currentConnection.wireSpline, 0f);
        Vector3 splineEnd = GetSplinePosition(currentConnection.wireSpline, 1f);

        float distanceToStart = Vector3.Distance(currentNode.Position, splineStart);
        float distanceToEnd = Vector3.Distance(currentNode.Position, splineEnd);

        if (distanceToStart <= distanceToEnd)
        {
            splineProgress = 0f;
            splineMoveDirection = 1f;
        }
        else
        {
            splineProgress = 1f;
            splineMoveDirection = -1f;
        }

        if (showDebugLog)
        {
            Debug.Log(
                "[Move Start] " +
                currentNode.name +
                " -> " +
                currentConnection.targetNode.name +
                " / Spline: " +
                currentConnection.wireSpline.name +
                " / Distance To Start: " +
                distanceToStart +
                " / Distance To End: " +
                distanceToEnd
            );
        }
    }

    private void MoveAlongSpline()
    {
        if (currentConnection == null)
        {
            if (showDebugLog)
            {
                Debug.LogWarning("[Move Failed] 이동 중 currentConnection이 null입니다.");
            }

            isMoving = false;
            return;
        }

        if (currentConnection.wireSpline == null)
        {
            if (showDebugLog)
            {
                Debug.LogWarning("[Move Failed] 이동 중 wireSpline이 null입니다.");
            }

            isMoving = false;
            return;
        }

        float wireLength = currentConnection.wireSpline.CalculateLength();

        if (wireLength <= 0.01f)
        {
            if (showDebugLog)
            {
                Debug.LogWarning("[Move Failed] Spline 길이가 너무 짧습니다: " + currentConnection.wireSpline.name);
            }

            isMoving = false;
            return;
        }

        float moveAmount = (moveSpeed / wireLength) * Time.deltaTime;

        splineProgress += splineMoveDirection * moveAmount;
        splineProgress = Mathf.Clamp01(splineProgress);

        Vector3 splinePosition = GetSplinePosition(currentConnection.wireSpline, splineProgress);
        transform.position = splinePosition;

        if (splineProgress <= 0f || splineProgress >= 1f)
        {
            FinishMove();
        }
    }

    private Vector3 GetSplinePosition(SplineContainer splineContainer, float progress)
    {
        return splineContainer.EvaluatePosition(progress);
    }

    private void FinishMove()
    {
        if (currentConnection != null && currentConnection.targetNode != null)
        {
            currentNode = currentConnection.targetNode;
            transform.position = currentNode.Position;

            if (showDebugLog)
            {
                Debug.Log("[Move Arrived] 현재 노드: " + currentNode.name);
            }
        }
        else
        {
            if (showDebugLog)
            {
                Debug.LogWarning("[Move Arrived Failed] 도착 처리 중 Target Node가 비어 있습니다.");
            }
        }

        currentConnection = null;
        isMoving = false;
        splineMoveDirection = 0f;

        TryUseBufferedInputAfterArrive();
    }

    private void TryUseBufferedInputAfterArrive()
    {
        if (!useInputBuffer)
        {
            ClearBufferedInput();
            return;
        }

        if (!hasBufferedInput)
        {
            return;
        }

        Vector3 direction = bufferedInputDirection;
        InputAxisType axis = bufferedInputAxis;

        ClearBufferedInput();

        if (showDebugLog)
        {
            Debug.Log(
                "[Input Buffer Use] 예약 입력으로 다음 이동 시도 / Axis: " +
                axis +
                " / Direction: " +
                direction
            );
        }

        TryStartMove(direction, axis);
    }

    public void SetCurrentNode(PoleNode node, bool snapToNode = true)
    {
        currentNode = node;
        currentConnection = null;
        isMoving = false;
        splineProgress = 0f;
        splineMoveDirection = 0f;

        ClearBufferedInput();

        if (snapToNode && currentNode != null)
        {
            transform.position = currentNode.Position;
        }

        if (showDebugLog)
        {
            if (currentNode != null)
            {
                Debug.Log("[Set Current Node] " + currentNode.name);
            }
            else
            {
                Debug.LogWarning("[Set Current Node] null");
            }
        }
    }
}