using System;
using System.Collections.Generic;
using Avalonia;

namespace AVABerzerk.Models;

public enum Side
{
    Top,
    Right,
    Bottom,
    Left
}

/// <summary>
/// One room of the maze, laid out the way the cabinet laid them out: an outer wall with a doorway in the
/// middle of each side, and eight pillars inside, from each of which a wall runs off in one of four
/// directions. The layout comes from the room's place in the maze, so going back into a room finds it as
/// it was. The doorway the humanoid came in by is shut behind it.
/// </summary>
public class Room
{
    public const double WallThickness = 8;

    /// <summary>The play area, on the 1024×768 screen.</summary>
    public static readonly Rect Arena = new(32, 56, 960, 600);
    public const int Columns = 5, Rows = 3;
    public static double CellWidth => Arena.Width / Columns;
    public static double CellHeight => Arena.Height / Rows;

    public int X { get; }
    public int Y { get; }
    public Side? EnteredFrom { get; }
    public List<Rect> Walls { get; } = new();

    /// <summary>The bar across the doorway the humanoid came in by, if any.</summary>
    public Rect? Door { get; }

    public Room(int x, int y, Side? enteredFrom)
    {
        X = x;
        Y = y;
        EnteredFrom = enteredFrom;
        var random = new Random(Seed(x, y));
        double t = WallThickness, a = Arena.X, b = Arena.Y, w = Arena.Width, h = Arena.Height;
        double cw = CellWidth, ch = CellHeight;

        // The outer wall, broken by a doorway in the middle of each side.
        Walls.Add(new Rect(a, b, cw * 2 + t, t));
        Walls.Add(new Rect(a + cw * 3, b, cw * 2 + t, t));
        Walls.Add(new Rect(a, b + h, cw * 2 + t, t));
        Walls.Add(new Rect(a + cw * 3, b + h, cw * 2 + t, t));
        Walls.Add(new Rect(a, b, t, ch + t));
        Walls.Add(new Rect(a, b + ch * 2, t, ch + t));
        Walls.Add(new Rect(a + w, b, t, ch + t));
        Walls.Add(new Rect(a + w, b + ch * 2, t, ch + t));

        // Eight pillars, each with a wall to the next pillar (or the outside wall) in one direction.
        for (int i = 1; i < Columns; i++)
        for (int j = 1; j < Rows; j++)
        {
            double px = a + i * cw, py = b + j * ch;
            Walls.Add(random.Next(4) switch
            {
                0 => new Rect(px, py - ch, t, ch + t),      // up
                1 => new Rect(px, py, cw + t, t),           // right
                2 => new Rect(px, py, t, ch + t),           // down
                _ => new Rect(px - cw, py, cw + t, t)       // left
            });
        }

        if (enteredFrom is { } side)
            Door = DoorwayBar(side);
    }

    private static int Seed(int x, int y) => unchecked(x * 73856093 ^ y * 19349663 ^ 0x5bd1e995);

    /// <summary>The bar that closes a doorway.</summary>
    public static Rect DoorwayBar(Side side)
    {
        double t = WallThickness, a = Arena.X, b = Arena.Y, cw = CellWidth, ch = CellHeight;
        return side switch
        {
            Side.Top => new Rect(a + cw * 2, b, cw + t, t),
            Side.Bottom => new Rect(a + cw * 2, b + Arena.Height, cw + t, t),
            Side.Left => new Rect(a, b + ch, t, ch + t),
            _ => new Rect(a + Arena.Width, b + ch, t, ch + t)
        };
    }

    /// <summary>Where the humanoid appears when it comes in through a doorway.</summary>
    public static Point EntryPoint(Side side) => side switch
    {
        Side.Top => new Point(Arena.Center.X, Arena.Top + 36),
        Side.Bottom => new Point(Arena.Center.X, Arena.Bottom - 30),
        Side.Left => new Point(Arena.Left + 30, Arena.Center.Y),
        _ => new Point(Arena.Right - 26, Arena.Center.Y)
    };

    public IEnumerable<Rect> Solid()
    {
        foreach (var wall in Walls) yield return wall;
        if (Door is { } door) yield return door;
    }

    /// <summary>Whether a box touches any wall (or the shut door).</summary>
    public bool Touches(Rect box)
    {
        foreach (var wall in Solid())
            if (wall.Intersects(box)) return true;
        return false;
    }

    /// <summary>The side a box has left the arena by, if it has gone out through a doorway.</summary>
    public static Side? OutsideBy(Rect box)
    {
        if (box.Bottom < Arena.Top) return Side.Top;
        if (box.Top > Arena.Bottom + WallThickness) return Side.Bottom;
        if (box.Right < Arena.Left) return Side.Left;
        if (box.Left > Arena.Right + WallThickness) return Side.Right;
        return null;
    }

    public static Side Opposite(Side side) => (Side)(((int)side + 2) % 4);
}
