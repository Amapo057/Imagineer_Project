using System;
using System.Reflection;
using UnityEngine;

public static class ProductionSeatingVerification
{
    public static void Run()
    {
        var root = new GameObject("Seating geometry verification");
        try
        {
            root.transform.SetPositionAndRotation(new Vector3(2, 1, -3), Quaternion.Euler(18, 47, 9));
            var anchor = root.AddComponent<AnchorManager>();
            typeof(AnchorManager).GetProperty("IsReady").SetValue(anchor, true);
            var seats = new ProductionSeating[2];
            for (int side = 0; side < 2; side++)
            {
                var holder = new GameObject("Seat " + side); holder.transform.SetParent(root.transform, false);
                var seat = holder.AddComponent<ProductionSeating>(); seats[side] = seat;
                seat.field = root.transform; seat.anchor = anchor;
                seat.marker = holder.AddComponent<MarkerWorldPos>();
                typeof(MarkerWorldPos).GetField("fieldRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(seat.marker, root);
                var camera = new GameObject("Head"); camera.transform.SetParent(holder.transform, false);
                seat.head = camera.AddComponent<Camera>();
                camera.transform.position = root.transform.TransformPoint(new Vector3(0, 1.6f, side == 0 ? -1 : 1));
                seat.localObjects = new Transform[4]; seat.remoteObjects = new Transform[4];
                for (int lane = 0; lane < 4; lane++)
                {
                    foreach (bool remote in new[] { false, true })
                    {
                        var item = new GameObject("Lane"); item.transform.SetParent(holder.transform, false);
                        item.transform.position = root.transform.TransformPoint(new Vector3(lane * 0.18f - 0.27f, 0, remote ? 0.24f : -0.24f));
                        (remote ? seat.remoteObjects : seat.localObjects)[lane] = item.transform;
                    }
                }
                typeof(ProductionSeating).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(seat, null);
                if (!seat.IsReady || seat.PositiveSide != (side == 1)) throw new Exception("Seat selection failed");
                var reject = typeof(MarkerWorldPos).GetMethod("isEnemyField", BindingFlags.Instance | BindingFlags.NonPublic);
                if ((bool)reject.Invoke(seat.marker, new object[] { seat.localObjects[0].position }) ||
                    !(bool)reject.Invoke(seat.marker, new object[] { seat.remoteObjects[0].position })) throw new Exception("Marker half filtering failed");
            }
            for (int lane = 0; lane < 4; lane++)
            {
                if (Vector3.Distance(seats[0].localObjects[lane].position, seats[1].remoteObjects[lane].position) > 0.0001f ||
                    Vector3.Distance(seats[1].localObjects[lane].position, seats[0].remoteObjects[lane].position) > 0.0001f)
                    throw new Exception("Peers disagree on physical lane " + lane);
            }
            Debug.Log("PRODUCTION_SEATING_PASS: opposite viewers, all four canonical lanes align, marker halves match, rotated/translated table");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
