using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Omnipad {
    // Names kept from the 2018 ViGEm API so the button-mapping code stays unchanged.
    public enum Xbox360Buttons { Up, Down, Left, Right, Start, Back, LeftThumb, RightThumb, LeftShoulder, RightShoulder, Guide, A, B, X, Y }
    public enum Xbox360Axes { LeftThumbX, LeftThumbY, RightThumbX, RightThumbY, LeftTrigger, RightTrigger }

    // Plain snapshot of an Xbox 360 pad state, filled in by Joycon and pushed to a VirtualPad.
    public class Xbox360Report {
        public readonly bool[] Buttons = new bool[15];
        public readonly short[] Axes = new short[6];

        public void SetButtonState(Xbox360Buttons b, bool v) { Buttons[(int)b] = v; }
        public void SetAxis(Xbox360Axes a, short v) { Axes[(int)a] = v; }
    }

    // One emulated Xbox 360 controller. It is owned by the physical controller's identity (its MAC address),
    // not by a particular USB or Bluetooth connection, so swapping cables does not make Steam see a new pad.
    public class VirtualPad : IDisposable {
        static readonly Xbox360Button[] ButtonMap = {
            Xbox360Button.Up, Xbox360Button.Down, Xbox360Button.Left, Xbox360Button.Right,
            Xbox360Button.Start, Xbox360Button.Back, Xbox360Button.LeftThumb, Xbox360Button.RightThumb,
            Xbox360Button.LeftShoulder, Xbox360Button.RightShoulder, Xbox360Button.Guide,
            Xbox360Button.A, Xbox360Button.B, Xbox360Button.X, Xbox360Button.Y,
        };
        static readonly Xbox360Axis[] StickMap = {
            Xbox360Axis.LeftThumbX, Xbox360Axis.LeftThumbY, Xbox360Axis.RightThumbX, Xbox360Axis.RightThumbY,
        };

        readonly IXbox360Controller target;
        readonly object sync = new object();
        bool connected, disposed;

        public string Mac;                 // identity key; null for Joy-Cons
        public DateTime? OrphanedSince;    // set while no physical connection is feeding this pad
        public Joycon Owner;               // the connection currently feeding this pad

        public event Action<byte, byte> Rumble; // (large motor, small motor)
        public event Action<int> Slot;          // Xbox player slot 0-3, from the LED feedback
        public int LastSlot = -1;

        public VirtualPad(ViGEmClient client) {
            target = client.CreateXbox360Controller();
            target.AutoSubmitReport = false;
            target.FeedbackReceived += (s, e) => {
                Rumble?.Invoke(e.LargeMotor, e.SmallMotor);
                if (e.LedNumber <= 3 && e.LedNumber != LastSlot) {
                    LastSlot = e.LedNumber;
                    Slot?.Invoke(e.LedNumber);
                }
            };
        }

        public void Connect() {
            lock (sync) {
                if (connected || disposed) return;
                target.Connect();
                connected = true;
            }
        }

        public void Disconnect() {
            lock (sync) {
                if (!connected) return;
                try { target.Disconnect(); } catch { }
                connected = false;
            }
        }

        public void SendReport(Xbox360Report r) {
            lock (sync) {
                if (!connected || disposed) return;
                for (int i = 0; i < ButtonMap.Length; i++)
                    target.SetButtonState(ButtonMap[i], r.Buttons[i]);
                for (int i = 0; i < StickMap.Length; i++)
                    target.SetAxisValue(StickMap[i], r.Axes[i]);
                target.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)(r.Axes[(int)Xbox360Axes.LeftTrigger] > 0 ? 255 : 0));
                target.SetSliderValue(Xbox360Slider.RightTrigger, (byte)(r.Axes[(int)Xbox360Axes.RightTrigger] > 0 ? 255 : 0));
                try { target.SubmitReport(); } catch { }
            }
        }

        // Releases every button and centers sticks, so nothing stays held while the controller switches connection.
        public void SendNeutral() {
            SendReport(new Xbox360Report());
        }

        public void Dispose() {
            lock (sync) {
                if (disposed) return;
                Disconnect();
                disposed = true;
            }
        }
    }
}
