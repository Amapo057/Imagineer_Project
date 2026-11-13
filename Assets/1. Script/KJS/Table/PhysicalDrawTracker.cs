using System.Collections.Generic;

// Only positive observations count: losing sight of a marker is never a draw.
public sealed class PhysicalDrawTracker
{
    sealed class Seen { public float insideAt, outsideAt = -1, last; public bool armed; }
    readonly Dictionary<int, Seen> seen = new Dictionary<int, Seen>();
    readonly HashSet<int> drawn = new HashSet<int>();
    public void Reset() { seen.Clear(); drawn.Clear(); }
    public void Confirm(int id) { drawn.Add(id); seen.Remove(id); }
    public bool Observe(int id, bool back, bool inside, bool outside, float now)
    {
        if (id <= 0 || drawn.Contains(id)) return false;
        if (!seen.TryGetValue(id, out var item))
        {
            if (!back || !inside) return false;
            item = new Seen { insideAt = now, last = now }; seen.Add(id, item);
        }
        if (now - item.last > 0.4f) { item.insideAt = now; item.outsideAt = -1; }
        item.last = now;
        if (inside)
        {
            item.outsideAt = -1;
            if (!back) { item.insideAt = now; return false; }
            if (now - item.insideAt >= 0.12f) item.armed = true;
            return false;
        }
        item.insideAt = now;
        if (!item.armed || !outside) { item.outsideAt = -1; return false; }
        if (item.outsideAt < 0) item.outsideAt = now;
        return now - item.outsideAt >= 0.12f;
    }
}
