using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Omnipad {
    // Turns the controller's gyro into mouse movement for aiming: turn left/right = mouse left/right,
    // tilt up/down = mouse up/down. Only the Pro Controller or the right Joy-Con of a pair aims.
    public static class GyroMouse {
        class State {
            public float biasYaw, biasPitch;  // slowly learned gyro offset while the controller is still
            public float accX, accY;          // sub-pixel remainders
            public bool toggledOn, toggleHeld;
        }
        static readonly Dictionary<Joycon, State> states = new Dictionary<Joycon, State>();

        static State Get(Joycon jc) {
            lock (states) {
                State s;
                if (!states.TryGetValue(jc, out s)) states[jc] = s = new State();
                return s;
            }
        }

        public static void Forget(Joycon jc) { lock (states) states.Remove(jc); }

        public static bool IsAimer(Joycon jc) => jc.isPro || (!jc.isLeft && jc.other != null);

        // One IMU sample (5 ms). Rates in degrees per second.
        public static void Sample(Joycon jc, float yawDps, float pitchDps, float dt) {
            if (Prefs.GyroMode == "off" || !IsAimer(jc)) return;
            var s = Get(jc);

            // Keep learning the resting offset so the cursor doesn't creep when the controller is put down.
            if (Math.Abs(yawDps - s.biasYaw) < 3f && Math.Abs(pitchDps - s.biasPitch) < 3f) {
                s.biasYaw += (yawDps - s.biasYaw) * 0.01f;
                s.biasPitch += (pitchDps - s.biasPitch) * 0.01f;
            }
            if (!Active(jc, s)) { s.accX = s.accY = 0; return; }

            float yaw = Deadband(yawDps - s.biasYaw), pitch = Deadband(pitchDps - s.biasPitch);
            s.accX += yaw * dt * Prefs.GyroSensitivity;
            s.accY += -pitch * dt * Prefs.GyroSensitivity * (Prefs.GyroInvertY ? -1 : 1);
        }

        // Called once per input report: sends the whole pixels gathered so far.
        public static void Flush(Joycon jc) {
            if (Prefs.GyroMode == "off" || !IsAimer(jc)) return;
            var s = Get(jc);
            int dx = (int)s.accX, dy = (int)s.accY;
            if (dx == 0 && dy == 0) return;
            s.accX -= dx; s.accY -= dy;
            var input = new INPUT { type = 0, mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = 0x0001 } }; // MOUSEEVENTF_MOVE (relative)
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        static float Deadband(float v) => Math.Abs(v) < 0.8f ? 0 : v;

        static bool Active(Joycon jc, State s) {
            Joycon left = jc.isPro ? jc : jc.other;   // in a pair, ZL lives on the left Joy-Con
            switch (Prefs.GyroMode) {
                case "always": return true;
                case "zl": return left != null && left.GetButton(Joycon.Button.SHOULDER_2);
                case "zr": return jc.isPro ? jc.GetButton(Joycon.Button.SHOULDER2_2) : jc.GetButton(Joycon.Button.SHOULDER_2);
                case "toggle": {
                    // Click both sticks together to switch aiming on/off.
                    bool both = jc.isPro ? jc.GetButton(Joycon.Button.STICK) && jc.GetButton(Joycon.Button.STICK2)
                                         : jc.GetButton(Joycon.Button.STICK) && left != null && left.GetButton(Joycon.Button.STICK);
                    if (both && !s.toggleHeld) s.toggledOn = !s.toggledOn;
                    s.toggleHeld = both;
                    return s.toggledOn;
                }
            }
            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        struct INPUT {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public MOUSEINPUT mi;   // x64 layout: union starts at offset 8
        }
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    }
}
