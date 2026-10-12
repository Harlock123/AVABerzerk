using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AVABerzerk.Models;

namespace AVABerzerk.Engine;

public enum GameState
{
    StartScreen,
    Playing,
    Paused,
    GameOver,
    Attract,
    EnterInitials,
    HighScores
}

/// <summary>The stick and the fire button, from the player or the demo pilot.</summary>
public readonly record struct Controls(Vector Direction, bool Fire);

/// <summary>
/// The game: the humanoid, the rooms of the maze and the robots in them (in GameEngine.Room), Evil Otto,
/// scoring and lives, and the title, attract and high-score cycle around it.
/// </summary>
public partial class GameEngine : IDisposable
{
    private static readonly Random Random = new();

    public const int StartingLives = 3;
    public const int MaxLives = 9;
    public const int FirstExtraLife = 5000;
    public const int ExtraLifeEvery = 10000;
    public const double BoltSpeed = 700;
    public const int MaxPlayerBolts = 2;
    private const double FireCooldown = 0.22;
    private const double DeathTime = 2.2;

    private const double GameOverInputDelay = 1.0;
    private const double AttractIdleDelay = 20.0;   // seconds on the title screen before the demo
    private const double AttractLength = 70.0;      // longest a demo runs
    private const double GameOverIdleDelay = 20.0;  // seconds on game over before the title returns
    private const double GameOverBeforeEntry = 2.5; // "GAME OVER" shows this long before initials entry
    private const double EntryInputGrace = 0.8;     // ignore input this long, so a mashed fire button doesn't enter letters
    private const double EntryTimeout = 45.0;       // idle this long in entry and the initials are taken as they are
    private const double HighScoresShowTime = 10.0; // the table stays up this long

    public static readonly Color HumanoidColor = Color.FromRgb(90, 255, 90);
    public static readonly Color WallColor = Color.FromRgb(40, 90, 255);
    public static readonly Color OttoColor = Color.FromRgb(255, 225, 40);

    public GameState State { get; private set; }
    public Humanoid Player { get; } = new();
    public int Score { get; private set; }
    public int Lives { get; private set; }

    /// <summary>Rooms passed through, for the high-score table.</summary>
    public int RoomsVisited { get; private set; }

    public string? Banner { get; private set; }
    public double BannerTime { get; private set; }
    public double Clock { get; private set; }
    public bool IsInPlay => State is GameState.Playing or GameState.Attract;

    public HighScoreTable Table { get; } = HighScoreTable.Load();
    public int HighScore => Table.TopScore;
    public InitialsEntry? Entry { get; private set; }
    public int HighlightIndex { get; private set; } = -1;
    public bool IsEntryPending => _entryPending;

    public InputHandler Input { get; } = new();
    public SoundManager Sound { get; } = new();

    private Controls _controls;
    private double _fireTimer;
    private int _nextExtraLife;

    private bool _wasStartPressed;
    private double _gameOverTimer;
    private bool _entryPending;
    private double _entryClock;
    private double _entryIdle;
    private double _padRepeat;
    private bool _wasPadUp, _wasPadDown, _wasPadLeft, _wasPadRight, _wasPadAccept, _wasPadDone, _wasLeftMouse;
    private double _tableTimer;
    private bool _tableLeadsToDemo;
    private double _idleTimer;
    private double _attractTimer;
    private bool _wasAnyInputLastFrame;
    private readonly AttractPilot _pilot = new();

    public GameEngine()
    {
        State = GameState.StartScreen;
        Room = new Room(0, 0, null);
        Level = 1;
        PopulateRoom(Room.Arena.Center);
    }

    public void Dispose()
    {
        // Closing the window mid-entry still keeps the score, under whatever initials are showing.
        if (_entryPending)
            CommitEntry();

        Sound.Dispose();
        Input.Dispose();
    }

    // ----- Starting, stopping and the attract cycle -----

    public void StartNewGame()
    {
        BeginRun(GameState.Playing);
        Sound.Say(Voice.CoinsDetected);
    }

    /// <summary>The game plays itself, silently, until its lives are gone or time runs out.</summary>
    public void StartAttract()
    {
        BeginRun(GameState.Attract);
        _attractTimer = AttractLength;
    }

    public void ReturnToTitle()
    {
        State = GameState.StartScreen;
        Sound.StopAllLoops();
        Sound.ClearVoice();
        Sound.IsSilenced = false;
        Level = 1;
        Room = new Room(0, 0, null);
        PopulateRoom(Room.Arena.Center);
        _idleTimer = 0;
        HighlightIndex = -1;
        Banner = null;
    }

    private void BeginRun(GameState state)
    {
        State = state;
        Sound.StopAllLoops();
        Sound.ClearVoice();
        Sound.IsSilenced = state == GameState.Attract;
        Score = 0;
        Lives = StartingLives;
        RoomsVisited = 0;
        Level = 0;
        _nextExtraLife = FirstExtraLife;
        _pilot.Reset();
        EnterRoom(0, 0, Side.Left);
    }

    private void ShowHighScores(bool leadsToDemo)
    {
        State = GameState.HighScores;
        _tableTimer = HighScoresShowTime;
        _tableLeadsToDemo = leadsToDemo;
        if (leadsToDemo)
            HighlightIndex = -1;
    }

    private void BeginInitials()
    {
        State = GameState.EnterInitials;
        Entry = new InitialsEntry(Table.LastInitials);
        _entryClock = 0;
        _entryIdle = 0;
        _wasPadUp = _wasPadDown = _wasPadLeft = _wasPadRight = _wasPadAccept = _wasPadDone = true;
        _wasLeftMouse = true;
    }

    private void CommitEntry()
    {
        _entryPending = false;
        string initials = Entry?.Initials ?? Table.LastInitials;
        HighlightIndex = Table.Insert(initials, Score, RoomsVisited);
        Table.Save();
        Entry = null;
    }

    private bool EntryAcceptsInput => State == GameState.EnterInitials && _entryClock >= EntryInputGrace && Entry != null;

    /// <summary>Keyboard input during initials entry, passed straight from the window.</summary>
    public void InitialsKey(Avalonia.Input.Key key)
    {
        if (!EntryAcceptsInput) return;
        var entry = Entry!;
        _entryIdle = 0;

        if (key >= Avalonia.Input.Key.A && key <= Avalonia.Input.Key.Z)
            entry.Type((char)('A' + (key - Avalonia.Input.Key.A)));
        else if (key >= Avalonia.Input.Key.D0 && key <= Avalonia.Input.Key.D9)
            entry.Type((char)('0' + (key - Avalonia.Input.Key.D0)));
        else if (key >= Avalonia.Input.Key.NumPad0 && key <= Avalonia.Input.Key.NumPad9)
            entry.Type((char)('0' + (key - Avalonia.Input.Key.NumPad0)));
        else if (key is Avalonia.Input.Key.OemPeriod or Avalonia.Input.Key.Decimal)
            entry.Type('.');
        else if (key == Avalonia.Input.Key.Up)
            entry.Cycle(1);
        else if (key == Avalonia.Input.Key.Down)
            entry.Cycle(-1);
        else if (key == Avalonia.Input.Key.Left || key == Avalonia.Input.Key.Back)
            entry.Back();
        else if (key == Avalonia.Input.Key.Right)
            entry.Move(1);
        else if (key == Avalonia.Input.Key.Space)
            entry.Advance();
        else if (key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Escape)
            entry.Finish();
    }

    // Gamepad and mouse input during initials entry: up/down dial the letter (holding repeats),
    // left/right move, a fire button accepts a letter, Start finishes. The wheel dials, a click accepts.
    private void UpdateInitials(double deltaTime)
    {
        _entryClock += deltaTime;
        _entryIdle += deltaTime;
        var entry = Entry!;

        bool up = Input.PadUp, down = Input.PadDown, left = Input.PadLeft, right = Input.PadRight;
        bool accept = Input.PadAccept, done = Input.PadDone, click = Input.IsLeftMousePressed;

        if (EntryAcceptsInput)
        {
            if (up || down || left || right || accept || done || click || Input.MouseWheelDelta != 0)
                _entryIdle = 0;

            int dial = 0;
            if ((up && !_wasPadUp) || (down && !_wasPadDown))
            {
                dial = up ? 1 : -1;
                _padRepeat = 0.4;
            }
            else if (up || down)
            {
                _padRepeat -= deltaTime;
                if (_padRepeat <= 0)
                {
                    dial = up ? 1 : -1;
                    _padRepeat = 0.12;
                }
            }
            if (Input.MouseWheelDelta != 0)
                dial = Math.Sign(Input.MouseWheelDelta);
            if (dial != 0)
                entry.Cycle(dial);

            if (left && !_wasPadLeft) entry.Back();
            if (right && !_wasPadRight) entry.Move(1);
            if ((accept && !_wasPadAccept) || (click && !_wasLeftMouse)) entry.Advance();
            if (done && !_wasPadDone) entry.Finish();
        }

        _wasPadUp = up; _wasPadDown = down; _wasPadLeft = left; _wasPadRight = right;
        _wasPadAccept = accept; _wasPadDone = done; _wasLeftMouse = click;

        if (_entryIdle >= EntryTimeout)
            entry.Finish();

        if (entry.IsDone)
        {
            CommitEntry();
            ShowHighScores(leadsToDemo: false);
        }
    }

    // ----- The frame -----

    public void Update(double deltaTime)
    {
        Clock += deltaTime;
        bool start = Input.IsFiring;
        bool startPressed = start && !_wasStartPressed;
        _wasStartPressed = start;
        bool newInput = Input.IsAnyInput && !_wasAnyInputLastFrame;
        _wasAnyInputLastFrame = Input.IsAnyInput;

        switch (State)
        {
            case GameState.Paused:
                return;

            case GameState.StartScreen:
            case GameState.HighScores:
                // The robots in the title's room shuffle about behind the text.
                IdleRobots(deltaTime);
                if (startPressed)
                {
                    StartNewGame();
                    return;
                }
                if (State == GameState.StartScreen)
                {
                    _idleTimer = Input.IsAnyInput ? 0 : _idleTimer + deltaTime;
                    if (_idleTimer >= AttractIdleDelay)
                        ShowHighScores(leadsToDemo: true);
                    return;
                }
                if (newInput)
                {
                    ReturnToTitle();
                    return;
                }
                _tableTimer -= deltaTime;
                if (_tableTimer <= 0)
                {
                    if (_tableLeadsToDemo)
                        StartAttract();
                    else
                        ReturnToTitle();
                }
                return;

            case GameState.EnterInitials:
                UpdateExplosions(deltaTime);
                UpdateInitials(deltaTime);
                return;

            case GameState.GameOver:
                UpdateExplosions(deltaTime);
                _gameOverTimer -= deltaTime;
                if (_entryPending)
                {
                    if (_gameOverTimer <= GameOverInputDelay - GameOverBeforeEntry)
                        BeginInitials();
                    return;
                }
                if (startPressed && _gameOverTimer <= 0)
                {
                    StartNewGame();
                    return;
                }
                _idleTimer = Input.IsAnyInput ? 0 : _idleTimer + deltaTime;
                if (_idleTimer >= GameOverIdleDelay)
                    ReturnToTitle();
                return;

            case GameState.Attract:
                if (startPressed)
                {
                    StartNewGame();
                    return;
                }
                if (newInput)
                {
                    ReturnToTitle();
                    return;
                }
                _attractTimer -= deltaTime;
                if (_attractTimer <= 0)
                {
                    ReturnToTitle();
                    return;
                }
                _controls = _pilot.Drive(this, deltaTime);
                break;

            default:
                _controls = PlayerControls();
                break;
        }

        BannerTime = Math.Max(0, BannerTime - deltaTime);
        if (Transition > 0)
        {
            UpdateTransition(deltaTime);
            return;
        }

        UpdatePlayer(deltaTime);
        UpdateRoom(deltaTime);
    }

    // The joystick, or the mouse: right button walks toward the pointer, left fires toward it.
    private Controls PlayerControls()
    {
        var stick = Input.Stick;
        bool fire = Input.IsFiring;
        if (stick.Length < 0.01 && (Input.IsMouseWalking || Input.IsMouseFiring))
        {
            var to = Sub(Input.Pointer, Player.Position);
            if (to.Length > 12)
                stick = to;
        }
        return new Controls(EightWay(stick), fire);
    }

    /// <summary>The offset from one point to another. (In Avalonia 12, Point minus Point is a Point, not a Vector.)</summary>
    public static Vector Sub(Point to, Point from) => new(to.X - from.X, to.Y - from.Y);

    /// <summary>Rounds a direction to the nearest of the eight the stick allows, as a unit vector (or zero).</summary>
    public static Vector EightWay(Vector v)
    {
        if (v.Length < 0.01) return default;
        double a = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
        return new Vector(Math.Round(Math.Cos(a), 6), Math.Round(Math.Sin(a), 6));
    }

    public void Pause()
    {
        if (State == GameState.Playing)
        {
            State = GameState.Paused;
            Sound.StopAllLoops();
        }
    }

    public void Resume()
    {
        if (State == GameState.Paused)
            State = GameState.Playing;
    }

    private void ShowBanner(string text, double time)
    {
        Banner = text;
        BannerTime = time;
    }

    private void AddScore(int points)
    {
        if (State == GameState.GameOver) return;
        Score += points;
        while (Score >= _nextExtraLife)
        {
            _nextExtraLife += ExtraLifeEvery;
            if (Lives < MaxLives)
            {
                Lives++;
                Sound.PlayExtraLife();
            }
        }
    }

    // ----- The humanoid -----

    private void UpdatePlayer(double deltaTime)
    {
        var p = Player;
        _fireTimer = Math.Max(0, _fireTimer - deltaTime);

        if (!p.IsAlive)
        {
            p.Dying += deltaTime;
            if (p.Dying >= DeathTime)
                AfterDeath();
            return;
        }

        var dir = _controls.Direction;
        p.IsShooting = _controls.Fire;
        p.IsMoving = false;

        if (_controls.Fire)
        {
            // Firing roots it to the spot; the stick aims.
            var aim = dir.Length > 0 ? dir : p.Facing;
            if (dir.Length > 0)
                p.Facing = dir;
            int mine = Bolts.Count(b => b.FromPlayer);
            if (_fireTimer <= 0 && mine < MaxPlayerBolts)
            {
                _fireTimer = FireCooldown;
                var muzzle = p.Position + new Vector(aim.X * 12, aim.Y * 10 - 4);
                Bolts.Add(new Bolt(muzzle, aim * BoltSpeed, fromPlayer: true));
                Sound.PlayShot();
            }
        }
        else if (dir.Length > 0)
        {
            p.Facing = dir;
            p.IsMoving = true;
            p.Position += dir * Humanoid.Speed * deltaTime;
            p.WalkCycle += Humanoid.Speed * deltaTime;
        }

        // The walls are electrified.
        if (Room.Touches(p.Box))
        {
            KillPlayer(electrocuted: true);
            return;
        }

        if (Room.OutsideBy(p.Box) is { } side)
            LeaveRoom(side);
    }

    private void KillPlayer(bool electrocuted)
    {
        if (!Player.IsAlive) return;
        Player.Dying = 0;
        Player.IsShooting = false;
        Sound.PlayZap();
        Sound.StopOtto();
        Sound.ClearVoice();
        Sound.Say(Voice.GotHumanoid);
        foreach (var bolt in Bolts) bolt.IsAlive = false;
        Explosions.Add(Explosion.CreateShip(Player.Position, HumanoidColor));
        if (electrocuted)
            Explosions.Add(Explosion.CreateSmall(Player.Position, WallColor));
    }

    // After the humanoid dies, play goes on in a fresh room, as on the cabinet.
    private void AfterDeath()
    {
        Lives--;
        if (Lives <= 0)
        {
            // A demo that runs out of lives hands straight back to the title.
            if (State == GameState.Attract)
            {
                _attractTimer = 0.01;
                return;
            }
            State = GameState.GameOver;
            Sound.StopAllLoops();
            _gameOverTimer = GameOverInputDelay;
            _idleTimer = 0;
            _entryPending = Table.Qualifies(Score);
            return;
        }

        var side = (Side)Random.Next(4);
        EnterRoom(Room.X + Random.Next(-3, 4), Room.Y + Random.Next(-3, 4), side);
    }

    private void UpdateExplosions(double deltaTime)
    {
        foreach (var e in Explosions) e.Update(deltaTime);
        Explosions.RemoveAll(e => e.IsFinished);
    }
}
