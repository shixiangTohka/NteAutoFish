using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace NteAutoFish
{
    enum State { Idle, Fight, Card, Stopped }

    /// <summary>
    /// Fishing state machine. Screen vision only: it never reads game memory and
    /// never injects into the process -- it looks at the screen and sends ordinary
    /// keyboard / mouse input.
    /// </summary>
    sealed class Bot
    {
        public event Action<string> Log;
        public event Action<State, int> Status;

        public int StallSeconds = 70;       // no bite this long -> stop
        public int CardThresholdPct = 16;   // % of bright pixels inside the centre box

        volatile bool _run;
        Thread _thread;
        int _caught;

        public bool Running { get { return _run; } }

        public void Start()
        {
            if (_run) return;
            if (_thread != null && _thread.IsAlive) _thread.Join(3000);   // let the old loop exit first
            _run = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public void Stop() { _run = false; }

        void Say(string s) { if (Log != null) Log(DateTime.Now.ToString("HH:mm:ss") + "  " + s); }
        void SetState(State s) { if (Status != null) Status(s, _caught); }

        void Loop()
        {
            Say("engine start (v1.0)");
            bool aHeld = false, dHeld = false;

            using (Grabber grab = new Grabber())
            {
                DateTime lastCast = DateTime.MinValue;
                DateTime lastActivity = DateTime.Now;
                DateTime lastBeat = DateTime.Now;
                DateTime fightEnd = DateTime.MinValue;
                DateTime lastBarSeen = DateTime.MinValue;
                DateTime fightStart = DateTime.Now;
                bool fighting = false;
                int bannerStreak = 0;
                int prepTries = 0;

                while (_run)
                {
                    try
                    {
                        GameWindow g = GameWindow.Find();
                        if (g == null) { Say("waiting for game window..."); Thread.Sleep(1000); continue; }
                        if (grab.Width != g.W || grab.Height != g.H)
                        {
                            grab.Ensure(g.W, g.H);
                            Say(string.Format("game window {0}x{1}", g.W, g.H));
                        }

                        if ((DateTime.Now - lastBeat).TotalSeconds > 15)
                        {
                            lastBeat = DateTime.Now;
                            Say("heartbeat: caught=" + _caught + " state=" + (fighting ? "fight" : "idle"));
                        }

                        EnsureFocus(g);
                        grab.Capture(g.X, g.Y);

                        BarInfo bar = Vision.ReadBar(grab.Buffer, g.W, g.H);
                        double card = Vision.CardScore(grab.Buffer, g.W, g.H);

                        bool barSeen = bar.Seen;
                        if (barSeen) { lastBarSeen = DateTime.Now; lastActivity = DateTime.Now; }

                        // ---------- fight (1s grace so one dropped frame does not end it) ----------
                        if (barSeen || (DateTime.Now - lastBarSeen).TotalSeconds < 1.0)
                        {
                            if (!fighting)
                            {
                                fighting = true;
                                fightStart = DateTime.Now;
                                Say("FIGHT  green=" + bar.GreenMin + ".." + bar.GreenMax + " tick=" + bar.Tick);
                                SetState(State.Fight);
                            }
                            if (barSeen) Steer(bar, ref aHeld, ref dHeld);
                            Thread.Sleep(6);
                            continue;
                        }

                        if (fighting)
                        {
                            fighting = false;
                            Release(ref aHeld, ref dHeld);
                            fightEnd = DateTime.Now;
                            Say("fight ended after " + (int)(DateTime.Now - fightStart).TotalSeconds + "s");
                            Thread.Sleep(2300);
                            continue;
                        }

                        // ---------- result card (only right after a fight) ----------
                        bool afterFight = (fightEnd != DateTime.MinValue) && (DateTime.Now - fightEnd).TotalSeconds < 12;
                        if (afterFight && card * 100.0 > CardThresholdPct)
                        {
                            Say("catch card (score=" + card.ToString("F2") + ")");
                            SetState(State.Card);
                            ClickRatio(g, 0.16, 0.84);
                            Thread.Sleep(900);
                            grab.Capture(g.X, g.Y);
                            if (Vision.CardScore(grab.Buffer, g.W, g.H) * 100.0 > CardThresholdPct)
                            {
                                ClickRatio(g, 0.51, 0.15);
                                Thread.Sleep(800);
                            }
                            _caught++;
                            fightEnd = DateTime.MinValue;
                            lastActivity = DateTime.Now;
                            Say("cycle done, caught=" + _caught);
                            SetState(State.Idle);
                            continue;
                        }
                        if (afterFight) fightEnd = DateTime.MinValue;

                        // ---------- out of bait: stop, never touch the shop ----------
                        bool safeIdle = (DateTime.Now - lastBarSeen).TotalSeconds > 1.5;
                        if (safeIdle && NoBaitBanner(grab, g.W, g.H)) bannerStreak++;
                        else bannerStreak = 0;
                        if (bannerStreak >= 4)
                        {
                            Say("OUT OF BAIT - stopping. Equip bait, then start again.");
                            break;
                        }

                        // ---------- fishing prep panel ----------
                        if (PrepPanel(grab, g.W, g.H))
                        {
                            if (prepTries++ >= 3) { Say("prep panel keeps coming back - stopping"); break; }
                            Say("prep panel -> start fishing (try " + prepTries + ")");
                            ClickRatio(g, 0.816, 0.878);
                            Thread.Sleep(1600);
                            lastActivity = DateTime.Now;
                            continue;
                        }
                        prepTries = 0;

                        // ---------- idle: cast / hook ----------
                        if ((DateTime.Now - lastActivity).TotalSeconds > StallSeconds)
                        {
                            Say("STALL " + StallSeconds + "s without a bite - stopping (check your bait)");
                            break;
                        }
                        if ((DateTime.Now - lastCast).TotalMilliseconds > 260)
                        {
                            Press("F", 45);
                            lastCast = DateTime.Now;
                        }
                        Thread.Sleep(40);
                    }
                    catch (Exception ex)
                    {
                        Say("EXCEPTION: " + ex.GetType().Name + " " + ex.Message);
                        Thread.Sleep(1000);
                    }
                }
            }

            _run = false;
            Say("engine stopped (caught=" + _caught + ")");
            SetState(State.Stopped);
        }

        static void Steer(BarInfo bar, ref bool aHeld, ref bool dHeld)
        {
            int err = bar.Tick - bar.Center;
            if (err < -15)
            {
                if (aHeld) { Native.Key(Native.Scan("A"), true); aHeld = false; }
                if (!dHeld) { Native.Key(Native.Scan("D"), false); dHeld = true; }
            }
            else if (err > 15)
            {
                if (dHeld) { Native.Key(Native.Scan("D"), true); dHeld = false; }
                if (!aHeld) { Native.Key(Native.Scan("A"), false); aHeld = true; }
            }
            else
            {
                if (dHeld) { Native.Key(Native.Scan("D"), true); dHeld = false; }
                if (aHeld) { Native.Key(Native.Scan("A"), true); aHeld = false; }
            }
        }

        static void Release(ref bool aHeld, ref bool dHeld)
        {
            if (aHeld) { Native.Key(Native.Scan("A"), true); aHeld = false; }
            if (dHeld) { Native.Key(Native.Scan("D"), true); dHeld = false; }
        }

        static void EnsureFocus(GameWindow g)
        {
            if (Native.GetForegroundWindow() == g.Handle) return;
            Native.Key(0x38, false); Thread.Sleep(30); Native.Key(0x38, true);   // ALT tap unlocks the foreground lock
            Thread.Sleep(50);
            Native.SetForegroundWindow(g.Handle);
            Thread.Sleep(120);
        }

        static void ClickRatio(GameWindow g, double rx, double ry)
        {
            Native.Click(g.X + (int)(g.W * rx), g.Y + (int)(g.H * ry));
        }

        static void Press(string key, int hold)
        {
            ushort sc = Native.Scan(key);
            Native.Key(sc, false);
            Thread.Sleep(hold);
            Native.Key(sc, true);
        }

        // ---------- small vision helpers ----------

        static double MeanRegion(Grabber grab, int w, int h, double x0, double x1, double y0, double y1)
        {
            int ax = (int)(w * x0), bx = (int)(w * x1);
            int ay = (int)(h * y0), by = (int)(h * y1);
            long sum = 0; int n = 0;
            byte[] buf = grab.Buffer;
            for (int y = ay; y < by; y += 3)
            {
                int row = y * w * 4;
                for (int x = ax; x < bx; x += 5)
                {
                    int i = row + x * 4;
                    sum += (buf[i] + buf[i + 1] + buf[i + 2]) / 3; n++;
                }
            }
            return n == 0 ? 0 : (double)sum / n;
        }

        static double BrightRatio(Grabber grab, int w, int h, double x0, double x1, double y0, double y1)
        {
            int ax = (int)(w * x0), bx = (int)(w * x1);
            int ay = (int)(h * y0), by = (int)(h * y1);
            int bright = 0, total = 0;
            byte[] buf = grab.Buffer;
            for (int y = ay; y < by; y += 3)
            {
                int row = y * w * 4;
                for (int x = ax; x < bx; x += 5)
                {
                    int i = row + x * 4;
                    if ((buf[i] + buf[i + 1] + buf[i + 2]) / 3 > 185) bright++;
                    total++;
                }
            }
            return total == 0 ? 0 : (double)bright / total;
        }

        static int BrightCount(Grabber grab, int w, int h, double x0, double x1, double y0, double y1, int threshold)
        {
            int ax = (int)(w * x0), bx = (int)(w * x1);
            int ay = (int)(h * y0), by = (int)(h * y1);
            int bright = 0;
            byte[] buf = grab.Buffer;
            for (int y = ay; y < by; y += 2)
            {
                int row = y * w * 4;
                for (int x = ax; x < bx; x += 2)
                {
                    int i = row + x * 4;
                    if ((buf[i] + buf[i + 1] + buf[i + 2]) / 3 > threshold) bright++;
                }
            }
            return bright;
        }

        static bool PrepPanel(Grabber grab, int w, int h)
        {
            double btn = BrightRatio(grab, w, h, 0.72, 0.96, 0.865, 0.905);
            double above = MeanRegion(grab, w, h, 0.72, 0.96, 0.795, 0.840);
            return btn > 0.45 && above < 120;
        }

        static bool NoBaitBanner(Grabber grab, int w, int h)
        {
            double mean = MeanRegion(grab, w, h, 0.02, 0.98, 0.465, 0.515);
            if (mean > 45) return false;
            return BrightCount(grab, w, h, 0.33, 0.67, 0.462, 0.518, 200) > 250;
        }
    }
}
