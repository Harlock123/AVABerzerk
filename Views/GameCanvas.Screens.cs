using System;
using Avalonia;
using Avalonia.Media;
using AVABerzerk.Engine;
using AVABerzerk.Models;

namespace AVABerzerk.Views;

// The score and lives below the room, and the title, entry and high-score screens.
public partial class GameCanvas
{
    private void DrawHud(DrawingContext context)
    {
        if (_engine.State == GameState.EnterInitials) return;

        double y = 678;
        context.DrawText(Text(_engine.Score.ToString(), 30, GameEngine.HumanoidColor), new Point(40, y));
        int spare = Math.Max(0, _engine.Lives - (_engine.Player.IsAlive ? 1 : 0));
        for (int i = 0; i < spare && i < 9; i++)
            DrawLifeIcon(context, new Point(48 + i * 22, y + 62));

        int high = _engine.State == GameState.Attract ? _engine.HighScore : Math.Max(_engine.HighScore, _engine.Score);
        DrawCentredText(context, $"HIGH SCORE {high}", 16, y + 4, Gold);
        if (_engine.IsInPlay || _engine.State == GameState.GameOver)
        {
            DrawCentredText(context, $"ROOM {_engine.RoomsVisited}", 14, y + 32, TextColor);
            DrawRightText(context, "ROBOTS", 13, ScreenWidth - 40, y + 2, TextColor);
            DrawRightText(context, _engine.Robots.Count.ToString(), 24, ScreenWidth - 40, y + 20, _engine.Kind.Color);
        }
        if (_engine.Sound.IsMuted || !_engine.Sound.IsVoiceOn)
            DrawRightText(context, (_engine.Sound.IsMuted ? "MUTED " : "") + (!_engine.Sound.IsVoiceOn ? "VOICE OFF" : ""), 11, ScreenWidth - 40, y + 60, TextColor);
    }

    private void DrawLifeIcon(DrawingContext context, Point c)
    {
        var color = GameEngine.HumanoidColor;
        context.DrawEllipse(GetBrush(color, 1), null, c + new Vector(0, -9), 3, 3);
        DrawGlowLine(context, c + new Vector(0, -6), c + new Vector(0, 2), color, 1, 2);
        DrawGlowLine(context, c + new Vector(0, 2), c + new Vector(-3, 9), color, 1, 1.8);
        DrawGlowLine(context, c + new Vector(0, 2), c + new Vector(3, 9), color, 1, 1.8);
        DrawGlowLine(context, c + new Vector(-5, -1), c + new Vector(5, -1), color, 1, 1.8);
    }

    // ----- Title screen -----

    private void DrawTitleScreen(DrawingContext context, bool withText)
    {
        DrawRoomView(context, withText ? 0.35 : 0.5);
        if (!withText) return;

        context.FillRectangle(GetBrush(Colors.Black, 0.55), new Rect(0, 0, ScreenWidth, ScreenHeight));
        DrawCentredText(context, "AVABERZERK", 78, 18, GameEngine.HumanoidColor);
        DrawCentredText(context, "INTRUDER ALERT!  INTRUDER ALERT!", 20, 112, AlertRed);

        // A robot and Otto, side by side, larger than life.
        using (context.PushTransform(Matrix.CreateScale(3, 3) * Matrix.CreateTranslation(ScreenWidth / 2 - 90, 245)))
            DrawRobot(context, new Point(0, 0), RobotKind.ForLevel(2 + (int)(Clock / 2) % 8).Color, Clock * 4, Clock, 1);
        using (context.PushTransform(Matrix.CreateScale(3, 3) * Matrix.CreateTranslation(ScreenWidth / 2 + 90, 250 - Math.Abs(Math.Sin(Clock * 3)) * 24)))
            DrawOtto(context, new Point(0, 0), 1);

        string[] legend =
        {
            $"ROBOT  {Robot.Points}",
            $"ROOM CLEARED  {GameEngine.RoomBonusPerRobot} PER ROBOT",
            "EVIL OTTO  CANNOT BE DESTROYED"
        };
        for (int i = 0; i < legend.Length; i++)
            DrawCentredText(context, legend[i], 16, 340 + i * 26, i == 2 ? GameEngine.OttoColor : Colors.White);

        var controls = new (string Keys, string Action)[]
        {
            ("Arrows / WASD", "Walk (8 ways)"),
            ("SPACE / CTRL + direction", "Stand and fire"),
            ("Mouse", "Right button walks to the pointer,"),
            ("", "left button fires at it"),
            ("Gamepad", "Stick walks, any button fires"),
            ("ESC  M  V", "Pause, mute, voice")
        };
        string[] orders =
        {
            "Escape the maze, room by room.",
            "The walls are electrified: don't touch.",
            "You can't walk while you shoot.",
            "Robots blow up on walls and on each",
            "other. Linger too long and Evil Otto",
            "comes bouncing in after you."
        };
        for (int i = 0; i < orders.Length; i++)
        {
            double y = 450 + i * 22;
            context.DrawText(Text(controls[i].Keys, 14, Colors.White), new Point(40, y));
            context.DrawText(Text(controls[i].Action, 14, TextColor), new Point(262, y));
            context.DrawText(Text(orders[i], 14, TextColor), new Point(590, y));
        }

        var best = _engine.Table.Entries.Count > 0 ? _engine.Table.Entries[0] : null;
        DrawCentredText(context, best == null ? "HIGH SCORE 0" : $"HIGH SCORE  {best.Initials.TrimEnd()}  {best.Score}", 18, 624, Gold);
        if (Blink(2))
            DrawCentredText(context, "Press SPACE or click to start", 26, 660, Colors.White);
    }

    // ----- High scores -----

    private string NewEntryHeadline => _engine.Score > _engine.HighScore ? "NEW HIGH SCORE" : "YOU MADE THE TOP TEN";

    private void DrawInitialsEntry(DrawingContext context)
    {
        var entry = _engine.Entry;
        if (entry == null) return;

        DrawPanel(context, new Rect(152, 110, 720, 560));
        DrawCentredText(context, NewEntryHeadline, 40, 140, Gold);
        DrawCentredText(context, $"SCORE {_engine.Score}    ROOMS {_engine.RoomsVisited}", 22, 200, TextColor);
        DrawCentredText(context, "ENTER YOUR INITIALS, HUMANOID", 24, 260, Colors.White);

        bool blinkOn = Blink(3);
        const double cell = 100;
        double left = (ScreenWidth - cell * HighScoreTable.InitialsLength) / 2;
        string initials = entry.Initials;

        for (int i = 0; i < initials.Length; i++)
        {
            bool current = i == entry.Cursor;
            char c = initials[i];
            var letter = Text(c == ' ' ? "_" : c.ToString(), 76, current ? Colors.White : Gold);
            double x = left + i * cell + (cell - letter.Width) / 2;
            context.DrawText(letter, new Point(x, 315));

            if (current)
            {
                var up = Text("^", 22, TextColor);
                var down = Text("v", 22, TextColor);
                context.DrawText(up, new Point(left + i * cell + (cell - up.Width) / 2, 300));
                context.DrawText(down, new Point(left + i * cell + (cell - down.Width) / 2, 418));
                if (blinkOn)
                    DrawGlowLine(context, new Point(left + i * cell + 18, 412), new Point(left + (i + 1) * cell - 18, 412), Colors.White, 1, 2.5);
            }
        }

        string[] help =
        {
            "Type your initials, or UP/DOWN to choose a letter",
            "and LEFT/RIGHT to move.  ENTER when done.",
            "Gamepad: D-pad to choose, X to accept, START when done",
            "Mouse: wheel to choose, click to accept"
        };
        double y = 480;
        foreach (var line in help)
        {
            DrawCentredText(context, line, 16, y, TextColor);
            y += 26;
        }
    }

    private void DrawHighScoreTable(DrawingContext context)
    {
        DrawPanel(context, new Rect(192, 60, 640, 640));
        DrawCentredText(context, "HIGH SCORES", 44, 80, GameEngine.HumanoidColor);

        const double rankX = 250, nameX = 340, scoreRight = 620, roomsX = 670;
        double y = 165;
        context.DrawText(Text("RANK", 16, TextColor), new Point(rankX, y));
        context.DrawText(Text("NAME", 16, TextColor), new Point(nameX, y));
        DrawRightText(context, "SCORE", 16, scoreRight, y, TextColor);
        context.DrawText(Text("ROOMS", 16, TextColor), new Point(roomsX, y));

        bool blinkOn = Blink(3);
        var entries = _engine.Table.Entries;
        y = 200;
        for (int i = 0; i < HighScoreTable.Size; i++)
        {
            bool highlight = i == _engine.HighlightIndex;
            var color = highlight ? (blinkOn ? Colors.White : Gold) : i == 0 ? Gold : Colors.White;

            string name = i < entries.Count ? entries[i].Initials : "---";
            string score = i < entries.Count ? entries[i].Score.ToString() : "0";
            string rooms = i < entries.Count ? entries[i].Rooms.ToString() : "-";

            context.DrawText(Text($"{i + 1}.", 24, color), new Point(rankX, y));
            context.DrawText(Text(name, 24, color), new Point(nameX, y));
            DrawRightText(context, score, 24, scoreRight, y, color);
            DrawRightText(context, rooms, 24, roomsX + 60, y, color);

            if (highlight)
                DrawGlowLine(context, new Point(rankX - 20, y + 15), new Point(rankX - 8, y + 15), color, 1, 2);
            y += 42;
        }

        if (Blink(2))
            DrawCentredText(context, "Press SPACE or click to play", 24, 650, Colors.White);
    }
}
