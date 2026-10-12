using System;
using Avalonia;
using Avalonia.Media;

namespace AVABerzerk.Models;

/// <summary>The player: the humanoid intruder. It walks in eight directions, and stands still to shoot.</summary>
public class Humanoid
{
    public const double Width = 14, Height = 30;
    public const double Speed = 115;

    public Point Position { get; set; }
    public Vector Facing { get; set; } = new(1, 0);
    public double WalkCycle { get; set; }
    public bool IsShooting { get; set; }
    public bool IsMoving { get; set; }

    /// <summary>Seconds since it was electrocuted or shot, or -1 while alive.</summary>
    public double Dying { get; set; } = -1;
    public bool IsAlive => Dying < 0;

    public Rect Box => new(Position.X - Width / 2, Position.Y - Height / 2, Width, Height);
}

/// <summary>A robot: it lumbers toward the humanoid, fires along any line it has to it, and dies on walls.</summary>
public class Robot
{
    public const double Width = 20, Height = 28;
    public const int Points = 50;

    public Point Position { get; set; }
    public Vector Moving { get; set; }
    public double ThinkTimer { get; set; }
    public double FireTimer { get; set; }
    public double EyePhase { get; set; }
    public bool IsAlive { get; set; } = true;

    public Rect Box => new(Position.X - Width / 2, Position.Y - Height / 2, Width, Height);
}

/// <summary>A bolt from the humanoid's gun or a robot's.</summary>
public class Bolt
{
    public Point Position { get; set; }
    public Vector Velocity { get; }
    public bool FromPlayer { get; }
    public bool IsAlive { get; set; } = true;
    public Point Start { get; }

    public Bolt(Point position, Vector velocity, bool fromPlayer)
    {
        Position = position;
        Start = position;
        Velocity = velocity;
        FromPlayer = fromPlayer;
    }

    /// <summary>A short streak along its path.</summary>
    public double Length => FromPlayer ? 16 : 10;
}

/// <summary>Evil Otto: a bouncing smiley face that comes through the walls after the humanoid. Nothing stops him.</summary>
public class Otto
{
    public const double Radius = 16;

    public Point Position { get; set; }
    public double Bounce { get; set; }
    public double Age { get; set; }

    /// <summary>Where he's drawn: bouncing up and down as he comes.</summary>
    public Point Drawn => new(Position.X, Position.Y - Math.Abs(Math.Sin(Bounce)) * 26);
}

/// <summary>What the robots are like in a given room: their colour, how many shots they may have in the air, and how fast.</summary>
public record RobotKind(Color Color, int MaxShots, double ShotSpeed, double Speed, double FireDelay)
{
    /// <summary>The cabinet's progression, room by room, then round again faster.</summary>
    public static RobotKind ForLevel(int level)
    {
        var kinds = new[]
        {
            new RobotKind(Color.FromRgb(200, 170, 40), 0, 0, 22, 99),        // dark yellow: they don't shoot
            new RobotKind(Color.FromRgb(230, 50, 40), 1, 300, 26, 2.4),      // red
            new RobotKind(Color.FromRgb(40, 170, 180), 2, 320, 30, 2.1),     // dark cyan
            new RobotKind(Color.FromRgb(60, 210, 60), 3, 340, 34, 1.9),      // green
            new RobotKind(Color.FromRgb(190, 70, 220), 4, 360, 38, 1.7),     // purple
            new RobotKind(Color.FromRgb(250, 230, 60), 5, 380, 42, 1.5),     // yellow
            new RobotKind(Color.FromRgb(240, 240, 240), 1, 560, 46, 1.3),    // white: one very fast shot
            new RobotKind(Color.FromRgb(120, 230, 255), 3, 470, 50, 1.2),    // light cyan
            new RobotKind(Color.FromRgb(255, 130, 200), 4, 500, 54, 1.1)     // pink
        };
        int index = (level - 1) % kinds.Length;
        int laps = (level - 1) / kinds.Length;
        var k = kinds[index];
        return k with { ShotSpeed = k.ShotSpeed * (1 + 0.1 * laps), Speed = k.Speed * (1 + 0.1 * laps), FireDelay = k.FireDelay / (1 + 0.15 * laps) };
    }
}
