using System;
using Avalonia;
using Avalonia.Media;
using AVABerzerk.Engine;
using AVABerzerk.Models;

namespace AVABerzerk.Views;

// The room: its electrified walls, the robots, the humanoid, the shots and Evil Otto.
public partial class GameCanvas
{
    private void DrawRoomView(DrawingContext context, double alpha = 1)
    {
        // Moving between rooms, the whole room scrolls out one way and the next scrolls in.
        var shift = new Vector();
        if (_engine.Transition > 0)
        {
            double s = _engine.Scroll;
            var dir = _engine.TransitionSide switch
            {
                Side.Left => new Vector(1, 0),
                Side.Right => new Vector(-1, 0),
                Side.Top => new Vector(0, 1),
                _ => new Vector(0, -1)
            };
            shift = new Vector(dir.X * Room.Arena.Width, dir.Y * Room.Arena.Height) * s;
        }

        using (context.PushClip(new Rect(0, 0, ScreenWidth, 668)))
        using (context.PushTransform(Matrix.CreateTranslation(shift.X, shift.Y)))
        {
            DrawWalls(context, alpha);
            foreach (var r in _engine.Robots)
                DrawRobot(context, r.Position, _engine.Kind.Color, r.EyePhase, r.Moving.Length > 0 ? _engine.Clock : 0, alpha);
            foreach (var b in _engine.Bolts)
                DrawBolt(context, b);
            if (_engine.State != GameState.StartScreen && _engine.State != GameState.HighScores && _engine.Transition <= 0)
                DrawHumanoid(context);
            if (_engine.Otto is { } otto)
                DrawOtto(context, otto.Drawn, 1);
            foreach (var e in _engine.Explosions)
            {
                double a = e.GetOpacity();
                foreach (var (p, q) in e.GetParticleLines())
                    DrawGlowLine(context, p, q, e.Color, a, 2);
            }
        }

        if (_engine.BannerTime > 0 && _engine.Banner != null && _engine.IsInPlay)
            DrawCentredText(context, _engine.Banner, 30, Room.Arena.Bottom - 70, Gold, alpha: Math.Min(1, _engine.BannerTime * 2));
    }

    // The walls hum with current: a steady blue, with a faint shimmer running along them.
    private void DrawWalls(DrawingContext context, double alpha)
    {
        double shimmer = 0.85 + 0.15 * Math.Sin(Clock * 9);
        foreach (var wall in _engine.Room.Walls)
        {
            context.FillRectangle(GetBrush(GameEngine.WallColor, 0.25 * alpha), wall.Inflate(4));
            context.FillRectangle(GetBrush(GameEngine.WallColor, shimmer * alpha), wall);
        }
        if (_engine.Room.Door is { } door)
        {
            context.FillRectangle(GetBrush(Color.FromRgb(255, 200, 60), 0.25 * alpha), door.Inflate(4));
            context.FillRectangle(GetBrush(Color.FromRgb(255, 200, 60), 0.9 * alpha), door);
        }
    }

    // The humanoid: a little green stick figure, legs going as it walks, arm out when it shoots.
    private void DrawHumanoid(DrawingContext context)
    {
        var p = _engine.Player;
        var c = p.Position;
        var color = GameEngine.HumanoidColor;

        if (!p.IsAlive)
        {
            // Electrocuted: it flashes through the colours, and a skeleton shows through.
            var colors = new[] { Colors.White, Color.FromRgb(255, 60, 60), Color.FromRgb(60, 120, 255), Color.FromRgb(255, 230, 60), color };
            color = colors[(int)(p.Dying * 18) % colors.Length];
            if (p.Dying > 1.6) return;
        }

        double step = Math.Sin(p.WalkCycle * 0.12) * (p.IsMoving ? 1 : 0);
        var head = c + new Vector(0, -11);
        context.DrawEllipse(GetBrush(color, 1), null, head, 4, 4);
        var neck = c + new Vector(0, -6);
        var hip = c + new Vector(0, 4);
        DrawGlowLine(context, neck, hip, color, 1, 2.5);

        // Legs.
        DrawGlowLine(context, hip, hip + new Vector(-4 - step * 3, 11), color, 1, 2.2);
        DrawGlowLine(context, hip, hip + new Vector(4 + step * 3, 11), color, 1, 2.2);

        // Arms: one points along the shot when firing.
        var shoulder = c + new Vector(0, -4);
        if (p.IsShooting)
        {
            var aim = p.Facing;
            DrawGlowLine(context, shoulder, shoulder + new Vector(aim.X * 11, aim.Y * 9), color, 1, 2.2);
            DrawGlowLine(context, shoulder, shoulder + new Vector(-aim.X * 4 - 2, 6), color, 1, 2.2);
        }
        else
        {
            DrawGlowLine(context, shoulder, shoulder + new Vector(-6 + step * 2, 7), color, 1, 2.2);
            DrawGlowLine(context, shoulder, shoulder + new Vector(6 - step * 2, 7), color, 1, 2.2);
        }
    }

    // A robot: a domed head with a scanning eye, a boxy body, and stubby legs.
    private void DrawRobot(DrawingContext context, Point c, Color color, double eyePhase, double walk, double alpha)
    {
        var dark = Color.FromRgb((byte)(color.R / 3), (byte)(color.G / 3), (byte)(color.B / 3));
        var dome = new[]
        {
            c + new Vector(-9, -6), c + new Vector(-9, -10), c + new Vector(-6, -14), c + new Vector(6, -14),
            c + new Vector(9, -10), c + new Vector(9, -6)
        };
        DrawShape(context, dome, dark, alpha, color, alpha, 1.5);

        // The eye slot, and the eye sliding back and forth along it.
        context.FillRectangle(GetBrush(Colors.Black, alpha), new Rect(c.X - 7, c.Y - 11, 14, 3));
        double eyeX = c.X + Math.Sin(eyePhase) * 5;
        context.FillRectangle(GetBrush(Colors.White, alpha), new Rect(eyeX - 2, c.Y - 11, 4, 3));

        var body = new[] { c + new Vector(-10, -5), c + new Vector(10, -5), c + new Vector(8, 7), c + new Vector(-8, 7) };
        DrawShape(context, body, dark, alpha, color, alpha, 1.5);
        DrawGlowLine(context, c + new Vector(-5, 0), c + new Vector(5, 0), color, 0.7 * alpha, 1.2);

        double step = walk > 0 ? Math.Sin(walk * 10) * 2 : 0;
        DrawGlowLine(context, c + new Vector(-5, 7), c + new Vector(-5 - step, 14), color, alpha, 2.2);
        DrawGlowLine(context, c + new Vector(5, 7), c + new Vector(5 + step, 14), color, alpha, 2.2);
    }

    private void DrawBolt(DrawingContext context, Bolt b)
    {
        var dir = b.Velocity / Math.Max(b.Velocity.Length, 1);
        var color = b.FromPlayer ? Color.FromRgb(200, 255, 200) : _engine.Kind.Color;
        DrawGlowLine(context, b.Position - dir * b.Length, b.Position, color, 1, b.FromPlayer ? 2.5 : 3);
    }

    // Evil Otto: a grinning yellow face, bouncing.
    private void DrawOtto(DrawingContext context, Point c, double alpha)
    {
        double r = Otto.Radius;
        context.DrawEllipse(GetBrush(GameEngine.OttoColor, 0.25 * alpha), null, c, r + 6, r + 6);
        context.DrawEllipse(GetBrush(GameEngine.OttoColor, alpha), GetPen(Color.FromRgb(255, 250, 200), alpha, 1.5), c, r, r);
        context.DrawEllipse(GetBrush(Colors.Black, alpha), null, c + new Vector(-6, -5), 2.8, 3.5);
        context.DrawEllipse(GetBrush(Colors.Black, alpha), null, c + new Vector(6, -5), 2.8, 3.5);
        var smile = new StreamGeometry();
        using (var g = smile.Open())
        {
            g.BeginFigure(c + new Vector(-10, 2), true);
            g.ArcTo(c + new Vector(10, 2), new Size(11, 9), 0, false, SweepDirection.CounterClockwise);
            g.EndFigure(true);
        }
        context.DrawGeometry(GetBrush(Colors.Black, alpha), null, smile);
    }
}
