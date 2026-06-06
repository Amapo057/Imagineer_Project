using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class PoleNetworkDebugVisualizer : MonoBehaviour
{
    [Header("표시 설정")]
    public bool showNodePoints = true;
    public bool showConnections = true;
    public bool showIsolatedNodes = true;
    public bool showSelectedNodeConnections = true;

    [Header("거리 표시")]
    public bool showMaxConnectDistance = false;
    public float maxConnectDistance = 30f;

    [Header("색상 설정")]
    public Color normalNodeColor = Color.yellow;
    public Color isolatedNodeColor = Color.red;
    public Color connectionColor = Color.green;
    public Color selectedConnectionColor = Color.cyan;
    public Color maxDistanceColor = new Color(1f, 1f, 1f, 0.15f);

    [Header("크기 설정")]
    public float nodeSphereSize = 0.25f;
    public float isolatedNodeSphereSize = 0.4f;
    public float selectedNodeSphereSize = 0.45f;

    private void OnDrawGizmos()
    {
        PoleNode[] nodes = FindObjectsByType<PoleNode>(FindObjectsSortMode.None);

        if (nodes == null || nodes.Length == 0)
        {
            return;
        }

        if (showConnections)
        {
            DrawAllConnections(nodes);
        }

        if (showNodePoints)
        {
            DrawAllNodes(nodes);
        }

        if (showIsolatedNodes)
        {
            DrawIsolatedNodes(nodes);
        }

        if (showMaxConnectDistance)
        {
            DrawMaxConnectDistance(nodes);
        }

        if (showSelectedNodeConnections)
        {
            DrawSelectedNodeConnections();
        }
    }

    private void DrawAllNodes(PoleNode[] nodes)
    {
        Gizmos.color = normalNodeColor;

        foreach (PoleNode node in nodes)
        {
            if (node == null)
            {
                continue;
            }

            Gizmos.DrawSphere(node.Position, nodeSphereSize);
        }
    }

    private void DrawAllConnections(PoleNode[] nodes)
    {
        HashSet<string> drawnPairs = new HashSet<string>();

        Gizmos.color = connectionColor;

        foreach (PoleNode node in nodes)
        {
            if (node == null || node.connections == null)
            {
                continue;
            }

            foreach (WireConnection connection in node.connections)
            {
                if (connection == null)
                {
                    continue;
                }

                if (connection.targetNode == null)
                {
                    continue;
                }

                string pairKey = GetPairKey(node, connection.targetNode);

                if (drawnPairs.Contains(pairKey))
                {
                    continue;
                }

                drawnPairs.Add(pairKey);

                Gizmos.DrawLine(node.Position, connection.targetNode.Position);
            }
        }
    }

    private void DrawIsolatedNodes(PoleNode[] nodes)
    {
        Gizmos.color = isolatedNodeColor;

        foreach (PoleNode node in nodes)
        {
            if (node == null)
            {
                continue;
            }

            bool isIsolated = node.connections == null || node.connections.Count == 0;

            if (isIsolated)
            {
                Gizmos.DrawSphere(node.Position, isolatedNodeSphereSize);
            }
        }
    }

    private void DrawMaxConnectDistance(PoleNode[] nodes)
    {
        Gizmos.color = maxDistanceColor;

        foreach (PoleNode node in nodes)
        {
            if (node == null)
            {
                continue;
            }

            Gizmos.DrawWireSphere(node.Position, maxConnectDistance);
        }
    }

    private void DrawSelectedNodeConnections()
    {
#if UNITY_EDITOR
        GameObject selectedObject = Selection.activeGameObject;

        if (selectedObject == null)
        {
            return;
        }

        PoleNode selectedNode = selectedObject.GetComponent<PoleNode>();

        if (selectedNode == null)
        {
            selectedNode = selectedObject.GetComponentInChildren<PoleNode>();
        }

        if (selectedNode == null)
        {
            selectedNode = selectedObject.GetComponentInParent<PoleNode>();
        }

        if (selectedNode == null)
        {
            return;
        }

        Gizmos.color = selectedConnectionColor;
        Gizmos.DrawSphere(selectedNode.Position, selectedNodeSphereSize);

        if (selectedNode.connections == null)
        {
            return;
        }

        foreach (WireConnection connection in selectedNode.connections)
        {
            if (connection == null)
            {
                continue;
            }

            if (connection.targetNode == null)
            {
                continue;
            }

            Gizmos.DrawLine(selectedNode.Position, connection.targetNode.Position);
            Gizmos.DrawSphere(connection.targetNode.Position, selectedNodeSphereSize * 0.75f);
        }
#endif
    }

    private string GetPairKey(PoleNode a, PoleNode b)
    {
        int aID = a.GetInstanceID();
        int bID = b.GetInstanceID();

        if (aID < bID)
        {
            return aID + "_" + bID;
        }

        return bID + "_" + aID;
    }
}