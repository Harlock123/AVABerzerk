using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using AVABerzerk.Models;

namespace AVABerzerk.Engine;

// The rooms of the maze, the robots in them, their shots, Evil Otto, and moving from room to room.
public partial class GameEngine
{
    public const int RoomBonusPerRobot = 10;
    private const double TransitionTime = 0.7;

    public Room Room { get; private set; } = null!;
    public int Level { get; private set; }
    public RobotKind Kind => RobotKind.ForLevel(Math.Max(1, Level));
    public List<Robot> Robots { get; } = new();
    public List<Bolt> Bolts { get; } = new();
    public Otto? Otto { get; private set; }
    public List<Explosion> Explosions { get; } = new();

    /// <summary>Counting down while the screen scrolls on to the next room.</summary>
    public double Transition { get; private set; }

    /// <summary>The side the humanoid left by, during the scroll.</summary>
    public Side TransitionSide { get; private set; }

    /// <summary>How far the room is scrolled during a change of room: 0 to 1 for the old room going, -1 to 0 for the new one coming.</summary>
    public double Scroll => Transition <= 0 ? 0 : Transition > TransitionTime / 2
        ? (TransitionTime - Transition) / (TransitionTime / 2)
        : -Transition / (TransitionTime / 2);

    private int _robotsAtStart;
    private bool _bonusGiven;
    private double _ottoTimer;
    private double _talkTimer;
    private double _graceTimer;
    private bool _roomBuilt;
    private (int X, int Y, Side From) _nextRoom;

    /// <summary>Walks in to a room from one side: lays it out, fills it with robots, starts Otto's clock.</summary>
    private void EnterRoom(int x, int y, Side from)
    {
        Level++;
        RoomsVisited++;
        Room = new Room(x, y, from);
        Player.Position = Room.EntryPoint(from);
        Player.Facing = from switch { Side.Left => new Vector(1, 0), Side.Right => new Vector(-1, 0), Side.Top => new Vector(0, 1), _ => new Vector(0, -1) };
        Player.Dying = -1;
        Player.IsShooting = false;
        PopulateRoom(Player.Position);
        _ottoTimer = Math.Max(6, 12 - 0.25 * Level) + _robotsAtStart * 0.5;
        _talkTimer = 0.9;
        _graceTimer = 1.2;
        _bonusGiven = false;
        Sound.StopOtto();
    }

    /// <summary>Places the robots, none too close to where the humanoid comes in, and none touching a wall.</summary>
    private void PopulateRoom(Point keepClear)
    {
        Robots.Clear();
        Bolts.Clear();
        Otto = null;
        Explosions.Clear();

        int count = 4 + Random.Next(Math.Min(8, 3 + Level / 2));
        int tries = 0;
        while (Robots.Count < count && tries++ < 400)
        {
            var cell = (X: Random.Next(Room.Columns), Y: Random.Next(Room.Rows));
            var p = new Point(Room.Arena.X + (cell.X + 0.2 + Random.NextDouble() * 0.6) * Room.CellWidth,
                              Room.Arena.Y + (cell.Y + 0.2 + Random.NextDouble() * 0.6) * Room.CellHeight);
            var robot = new Robot { Position = p, ThinkTimer = Random.NextDouble(), FireTimer = 1 + Random.NextDouble() * 2, EyePhase = Random.NextDouble() * 6 };
            var margin = robot.Box.Inflate(10);
            if (Room.Touches(margin)) continue;
            if (Sub(p, keepClear).Length < 230) continue;
            if (Robots.Any(r => Sub(r.Position, p).Length < 50)) continue;
            Robots.Add(robot);
        }
        _robotsAtStart = Robots.Count;
    }

    /// <summary>The humanoid has walked out through a doorway: a bonus if no robot is left, a jeer if some are.</summary>
    private void LeaveRoom(Side side)
    {
        if (Robots.Count > 0)
        {
            Sound.ClearVoice();
            Sound.Say(Voice.Chicken);
        }
        Sound.StopOtto();
        Sound.PlayExit();

        int dx = side == Side.Left ? -1 : side == Side.Right ? 1 : 0;
        int dy = side == Side.Top ? -1 : side == Side.Bottom ? 1 : 0;
        _nextRoom = (Room.X + dx, Room.Y + dy, Room.Opposite(side));
        TransitionSide = side;
        Transition = TransitionTime;
        _roomBuilt = false;
    }

    // The old room scrolls away; halfway, the new room is built and scrolls in.
    private void UpdateTransition(double deltaTime)
    {
        Transition -= deltaTime;
        if (!_roomBuilt && Transition <= TransitionTime / 2)
        {
            _roomBuilt = true;
            EnterRoom(_nextRoom.X, _nextRoom.Y, _nextRoom.From);
        }
        if (Transition <= 0)
            Transition = 0;
    }

    private void UpdateRoom(double deltaTime)
    {
        _graceTimer = Math.Max(0, _graceTimer - deltaTime);
        UpdateRobots(deltaTime);
        UpdateBolts(deltaTime);
        UpdateOtto(deltaTime);
        UpdateExplosions(deltaTime);
        Robots.RemoveAll(r => !r.IsAlive);
        Bolts.RemoveAll(b => !b.IsAlive);

        // Every robot gone: the room's bonus.
        if (!_bonusGiven && Robots.Count == 0 && _robotsAtStart > 0 && Player.IsAlive)
        {
            _bonusGiven = true;
            int bonus = _robotsAtStart * RoomBonusPerRobot;
            AddScore(bonus);
            ShowBanner($"BONUS {bonus}", 2);
            Sound.PlayBonus();
        }

        // The robots talk now and then.
        _talkTimer -= deltaTime;
        if (_talkTimer <= 0 && Robots.Count > 0 && Player.IsAlive)
        {
            _talkTimer = 6 + Random.NextDouble() * 6;
            var lines = new[] { Voice.IntruderAlert, Voice.MustNotEscape, Voice.KillHumanoid, Voice.DestroyIntruder, Voice.ChargeAttack, Voice.ShootIt, Voice.GetHumanoid };
            Sound.Say(lines[Random.Next(lines.Length)]);
        }
    }

    // ----- Robots -----

    private void UpdateRobots(double deltaTime)
    {
        var kind = Kind;
        bool playerAlive = Player.IsAlive;
        int robotBolts = Bolts.Count(b => !b.FromPlayer);

        foreach (var r in Robots)
        {
            r.EyePhase += deltaTime * 4;
            r.ThinkTimer -= deltaTime;
            if (r.ThinkTimer <= 0)
            {
                // Lumber toward the humanoid, roughly, or stand and watch for a moment.
                r.ThinkTimer = 0.5 + Random.NextDouble() * 1.0;
                var to = Sub(Player.Position, r.Position);
                if (!playerAlive || Random.NextDouble() < 0.3)
                    r.Moving = default;
                else
                    r.Moving = EightWay(new Vector(Math.Abs(to.X) < 14 ? 0 : to.X, Math.Abs(to.Y) < 14 ? 0 : to.Y));
            }
            r.Position += r.Moving * kind.Speed * deltaTime;

            // Fire along any of the eight lines that runs to the humanoid, if there's no wall in between.
            r.FireTimer -= deltaTime;
            if (playerAlive && kind.MaxShots > robotBolts && r.FireTimer <= 0 && _graceTimer <= 0 && Transition <= 0)
            {
                var to = Sub(Player.Position, r.Position);
                bool lined = Math.Abs(to.X) < 12 || Math.Abs(to.Y) < 12 || Math.Abs(Math.Abs(to.X) - Math.Abs(to.Y)) < 16;
                if (lined && to.Length < 700 && ClearLine(r.Position, Player.Position))
                {
                    var dir = EightWay(to);
                    Bolts.Add(new Bolt(r.Position + dir * 14, dir * kind.ShotSpeed, fromPlayer: false));
                    robotBolts++;
                    r.FireTimer = kind.FireDelay * (0.7 + Random.NextDouble() * 0.6);
                    Sound.PlayRobotShot();
                }
            }
        }

        // Robots that walk into walls, into each other, or into the humanoid, blow up.
        foreach (var r in Robots)
        {
            if (!r.IsAlive) continue;
            if (Room.Touches(r.Box))
            {
                DestroyRobot(r, scored: true);
                continue;
            }
            foreach (var other in Robots)
            {
                if (other != r && other.IsAlive && other.Box.Intersects(r.Box))
                {
                    DestroyRobot(r, scored: true);
                    DestroyRobot(other, scored: true);
                    break;
                }
            }
            if (r.IsAlive && Player.IsAlive && r.Box.Intersects(Player.Box))
            {
                DestroyRobot(r, scored: false);
                KillPlayer(electrocuted: false);
            }
        }
    }

    // On the title screen the robots just stand in their room, eyes scanning.
    private void IdleRobots(double deltaTime)
    {
        foreach (var r in Robots)
            r.EyePhase += deltaTime * 4;
        UpdateExplosions(deltaTime);
    }

    private void DestroyRobot(Robot r, bool scored)
    {
        if (!r.IsAlive) return;
        r.IsAlive = false;
        if (scored)
            AddScore(Robot.Points);
        Explosions.Add(Explosion.CreateShip(r.Position, Kind.Color));
        Sound.PlayRobotBoom();
    }

    /// <summary>Whether a straight line between two points crosses no wall.</summary>
    public bool ClearLine(Point a, Point b)
    {
        var d = Sub(b, a);
        int steps = Math.Max(1, (int)(d.Length / 6));
        for (int i = 1; i < steps; i++)
        {
            var p = a + d * ((double)i / steps);
            var probe = new Rect(p.X - 1, p.Y - 1, 2, 2);
            if (Room.Touches(probe)) return false;
        }
        return true;
    }

    // ----- Bolts -----

    private void UpdateBolts(double deltaTime)
    {
        foreach (var b in Bolts)
        {
            // Small steps, so a fast bolt can't hop a wall or a robot.
            const int steps = 3;
            for (int s = 0; s < steps && b.IsAlive; s++)
            {
                b.Position += b.Velocity * (deltaTime / steps);
                var tip = new Rect(b.Position.X - 2, b.Position.Y - 2, 4, 4);

                if (Room.Touches(tip) || !Room.Arena.Inflate(20).Contains(b.Position))
                {
                    b.IsAlive = false;
                    if (b.FromPlayer && Room.Touches(tip))
                        Sound.PlayWallHit();
                    Explosions.Add(Explosion.CreateSmall(b.Position, Room.Touches(tip) ? GameEngine.WallColor : Kind.Color));
                    break;
                }

                var robot = Robots.FirstOrDefault(r => r.IsAlive && r.Box.Intersects(tip));
                if (robot != null && (b.FromPlayer || Sub(b.Position, b.Start).Length > 20))
                {
                    b.IsAlive = false;
                    DestroyRobot(robot, scored: true);
                    break;
                }

                if (!b.FromPlayer && Player.IsAlive && Player.Box.Intersects(tip))
                {
                    b.IsAlive = false;
                    KillPlayer(electrocuted: false);
                    break;
                }

                // Otto soaks up shots and keeps coming.
                if (b.FromPlayer && Otto != null && Sub(b.Position, Otto.Drawn).Length < Otto.Radius)
                {
                    b.IsAlive = false;
                    break;
                }
            }
        }
    }

    // ----- Evil Otto -----

    // Linger too long and Otto comes bouncing in from the doorway the humanoid used, straight through the
    // walls. Nothing stops him; he's quicker once the robots are all gone.
    private void UpdateOtto(double deltaTime)
    {
        if (!Player.IsAlive)
            return;

        if (Otto == null)
        {
            _ottoTimer -= deltaTime;
            if (_ottoTimer <= 0 && Room.EnteredFrom is { } side)
            {
                Otto = new Otto { Position = Room.EntryPoint(side) };
                Sound.PlayOtto();
                Sound.StartOtto();
            }
            return;
        }

        var o = Otto;
        o.Age += deltaTime;
        o.Bounce += deltaTime * 6;
        double speed = Robots.Count == 0 ? 135 : Math.Min(110, 70 + 2 * Level);
        var to = Sub(Player.Position, o.Position);
        if (to.Length > 1)
            o.Position += to / to.Length * speed * deltaTime;

        if (Sub(Player.Position, o.Drawn).Length < Otto.Radius + 10)
        {
            KillPlayer(electrocuted: false);
            return;
        }
        foreach (var r in Robots)
            if (r.IsAlive && Sub(r.Position, o.Drawn).Length < Otto.Radius + 10)
                DestroyRobot(r, scored: false);
    }
}
