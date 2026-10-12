using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using AVABerzerk.Models;
using static AVABerzerk.Engine.GameEngine;

namespace AVABerzerk.Engine;

/// <summary>
/// Plays the humanoid in attract mode. It finds its way about the room on a coarse grid that keeps clear of
/// the walls, lines up on robots along the eight firing lines and shoots, and once the room is clear (or
/// Evil Otto turns up) it heads for a doorway. Good enough to put on a show; it still gets caught.
/// </summary>
public class AttractPilot
{
    private const double GridStep = 16;
    private const double Clearance = 22;

    private static readonly Random Random = new();
    private double _hesitation;
    private bool _fireToggle;
    private int _room;
    private double _inRoom;
    private double _patience;

    public void Reset() => _hesitation = 0;

    public Controls Drive(GameEngine engine, double deltaTime)
    {
        var p = engine.Player;
        if (!p.IsAlive) return default;
        _fireToggle = !_fireToggle;

        _hesitation -= deltaTime;
        if (_hesitation < -5 && Random.NextDouble() < deltaTime * 0.2)
            _hesitation = 0.4 + Random.NextDouble() * 0.5;
        if (_hesitation > 0) return default;

        var room = engine.Room;
        if (engine.RoomsVisited != _room)
        {
            _room = engine.RoomsVisited;
            _inRoom = 0;
            _patience = 8 + Random.NextDouble() * 5;
        }
        _inRoom += deltaTime;

        // It moves on when the room's clear, when Otto shows up, or when it has spent long enough here.
        bool leave = engine.Robots.Count == 0 || engine.Otto != null || _inRoom > _patience;

        // A robot's shot coming straight at it: step aside.
        foreach (var bolt in engine.Bolts.Where(b => !b.FromPlayer))
        {
            var to = Sub(p.Position, bolt.Position);
            if (to.Length > 220) continue;
            var v = bolt.Velocity / Math.Max(bolt.Velocity.Length, 1);
            double along = to.X * v.X + to.Y * v.Y;
            double across = Math.Abs(to.X * v.Y - to.Y * v.X);
            if (along > 0 && across < 22)
                return new Controls(SafeStep(room, p.Position, new Vector(-v.Y, v.X)), false);
        }

        if (!leave)
        {
            // A robot on a firing line with a clear shot: stand and fire.
            foreach (var r in engine.Robots.OrderBy(r => Sub(r.Position, p.Position).Length))
            {
                var to = Sub(r.Position, p.Position);
                bool lined = Math.Abs(to.X) < 8 || Math.Abs(to.Y) < 8 || Math.Abs(Math.Abs(to.X) - Math.Abs(to.Y)) < 10;
                if (lined && to.Length < 650 && engine.ClearLine(p.Position, r.Position))
                    return new Controls(GameEngine.EightWay(to), _fireToggle);
            }
        }

        // Otherwise walk: to a doorway when leaving, or to a spot lined up with the nearest robot.
        Point goal;
        if (leave)
        {
            // The doorway it can reach well before Otto can.
            var otto = engine.Otto;
            var exits = new[] { Side.Top, Side.Right, Side.Bottom, Side.Left }.Where(s => s != room.EnteredFrom)
                .Select(ExitPoint)
                .OrderBy(e => Sub(e, p.Position).Length - (otto == null ? 0 : 0.9 * Sub(e, otto.Position).Length))
                .ToList();
            goal = exits[0];
            // Close to the doorway, or already in it: just keep walking out.
            var inner = Room.Arena.Deflate(4);
            bool inDoorway = !inner.Contains(p.Position);
            if (inDoorway)
                goal = exits.OrderBy(e => Sub(e, p.Position).Length).First();
            if (inDoorway || Sub(goal, p.Position).Length < 30)
                return new Controls(GameEngine.EightWay(Outward(goal)), false);
        }
        else
        {
            var target = engine.Robots.OrderBy(r => Sub(r.Position, p.Position).Length).First();
            goal = new Point(target.Position.X, p.Position.Y);
            if (Math.Abs(target.Position.X - p.Position.X) > Math.Abs(target.Position.Y - p.Position.Y))
                goal = new Point(p.Position.X, target.Position.Y);
            if (Blocked(room, goal)) goal = target.Position;
        }

        // Plan round Otto, and round the robots, as well as the walls.
        var hazards = engine.Robots.Select(r => (r.Position, 34.0)).ToList();
        if (engine.Otto is { } o)
        {
            hazards.Add((o.Position, 95.0));
            hazards.Add((o.Drawn, 95.0));
        }
        var step = NextStep(room, p.Position, goal, hazards);
        // Keep away from robots close by rather than walk into them.
        foreach (var r in engine.Robots)
        {
            var away = Sub(p.Position, r.Position);
            if (away.Length < 45)
                return new Controls(SafeStep(room, p.Position, away), false);
        }
        return new Controls(SafeStep(room, p.Position, step), false);
    }

    /// <summary>
    /// The direction nearest the one wanted that won't put the humanoid against a wall in the next few
    /// steps, or no movement at all if every way is blocked.
    /// </summary>
    private static Vector SafeStep(Room room, Point from, Vector wanted)
    {
        if (wanted.Length < 0.01) return default;
        double a0 = Math.Atan2(wanted.Y, wanted.X);
        foreach (int k in new[] { 0, 1, -1, 2, -2, 3, -3, 4 })
        {
            var dir = GameEngine.EightWay(new Vector(Math.Cos(a0 + k * Math.PI / 4), Math.Sin(a0 + k * Math.PI / 4)));
            var ahead = from + dir * 10;
            var box = new Rect(ahead.X - Humanoid.Width / 2 - 4, ahead.Y - Humanoid.Height / 2 - 4, Humanoid.Width + 8, Humanoid.Height + 8);
            if (!room.Touches(box) || Room.OutsideBy(box) != null)
                return dir;
        }
        return default;
    }

    private static Point ExitPoint(Side side) => side switch
    {
        Side.Top => new Point(Room.Arena.Center.X, Room.Arena.Top + 10),
        Side.Bottom => new Point(Room.Arena.Center.X, Room.Arena.Bottom - 6),
        Side.Left => new Point(Room.Arena.Left + 10, Room.Arena.Center.Y),
        _ => new Point(Room.Arena.Right - 6, Room.Arena.Center.Y)
    };

    private static Vector Outward(Point exit)
    {
        var c = Room.Arena.Center;
        var d = Sub(exit, c);
        return Math.Abs(d.X) > Math.Abs(d.Y) ? new Vector(Math.Sign(d.X), 0) : new Vector(0, Math.Sign(d.Y));
    }

    private static bool Blocked(Room room, Point p) =>
        room.Touches(new Rect(p.X - Clearance / 2 - 4, p.Y - Clearance, Clearance + 8, Clearance * 2));

    /// <summary>The first step of the shortest path on a grid of safe points, as one of the eight directions.</summary>
    private static Vector NextStep(Room room, Point from, Point to, List<(Point At, double Radius)> hazards)
    {
        int cols = (int)(Room.Arena.Width / GridStep), rows = (int)(Room.Arena.Height / GridStep);
        (int, int) Cell(Point p) => (Math.Clamp((int)((p.X - Room.Arena.X) / GridStep), 0, cols - 1),
                                     Math.Clamp((int)((p.Y - Room.Arena.Y) / GridStep), 0, rows - 1));
        Point Centre((int X, int Y) c) => new(Room.Arena.X + (c.X + 0.5) * GridStep, Room.Arena.Y + (c.Y + 0.5) * GridStep);

        var start = Cell(from);
        var goal = Cell(to);
        var came = new Dictionary<(int, int), (int, int)>();
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue(start);
        came[start] = start;
        var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };
        bool found = false;
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (c == goal) { found = true; break; }
            foreach (var (dx, dy) in dirs)
            {
                var n = (c.X + dx, c.Y + dy);
                if (n.Item1 < 0 || n.Item2 < 0 || n.Item1 >= cols || n.Item2 >= rows || came.ContainsKey(n)) continue;
                var centre = Centre(n);
                if (Blocked(room, centre) || hazards.Any(h => Sub(centre, h.At).Length < h.Radius)) continue;
                came[n] = c;
                queue.Enqueue(n);
            }
        }
        if (!found) return GameEngine.EightWay(Sub(to, from));

        var step = goal;
        while (came[step] != start)
            step = came[step];
        return GameEngine.EightWay(Sub(Centre(step), from));
    }
}
