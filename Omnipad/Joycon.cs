using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;

namespace Omnipad {
    public class Joycon {
        float timing = 120.0f;

        public string path = String.Empty;
        public bool isPro = false;
        public bool isUSB = false;
        public string mac; // Bluetooth MAC as 12 uppercase hex chars; identifies the physical controller on USB and Bluetooth
        public bool retired = false; // closed because the same controller is now connected another way
        public Joycon other;

        public bool send = true;

        public enum DebugType : int {
            NONE,
            ALL,
            COMMS,
            THREADING,
            IMU,
            RUMBLE,
        };
        public DebugType debug_type = DebugType.NONE;
        public bool isLeft;
        public enum state_ : uint {
            NOT_ATTACHED,
            DROPPED,
            NO_JOYCONS,
            ATTACHED,
            INPUT_MODE_0x30,
            IMU_DATA_OK,
        };
        public state_ state;
        public enum Button : int {
            DPAD_DOWN = 0,
            DPAD_RIGHT = 1,
            DPAD_LEFT = 2,
            DPAD_UP = 3,
            SL = 4,
            SR = 5,
            MINUS = 6,
            HOME = 7,
            PLUS = 8,
            CAPTURE = 9,
            STICK = 10,
            SHOULDER_1 = 11,
            SHOULDER_2 = 12,

            // For pro controller
            B = 13,
            A = 14,
            Y = 15,
            X = 16,
            STICK2 = 17,
            SHOULDER2_1 = 18,
            SHOULDER2_2 = 19,
        };
        private bool[] buttons_down = new bool[20];
        private bool[] buttons_up = new bool[20];
        private bool[] buttons = new bool[20];
        private bool[] down_ = new bool[20];

        private float[] stick = { 0, 0 };
        private float[] stick2 = { 0, 0 };

        private IntPtr handle;

        byte[] default_buf = { 0x0, 0x1, 0x40, 0x40, 0x0, 0x1, 0x40, 0x40 };

        private byte[] stick_raw = { 0, 0, 0 };
        private UInt16[] stick_cal = { 0, 0, 0, 0, 0, 0 };
        private UInt16 deadzone;
        private UInt16[] stick_precal = { 0, 0 };

        private byte[] stick2_raw = { 0, 0, 0 };
        private UInt16[] stick2_cal = { 0, 0, 0, 0, 0, 0 };
        private UInt16 deadzone2;
        private UInt16[] stick2_precal = { 0, 0 };

        private bool stop_polling = false;
        private int timestamp;
        private bool first_imu_packet = true;
        private bool imu_enabled = false;
        private Int16[] acc_r = { 0, 0, 0 };
        private Int16[] acc_neutral = { 0, 0, 0 };
        private Int16[] acc_sensiti = { 0, 0, 0 };
        private Vector3 acc_g;

        private Int16[] gyr_r = { 0, 0, 0 };
        private Int16[] gyr_neutral = { 0, 0, 0 };
        private Int16[] gyr_sensiti = { 0, 0, 0 };
        private Vector3 gyr_g;

        private Int16[] pro_hor_offset = { -710, 0, 0 };
        private Int16[] left_hor_offset = { 0, 0, 0 };
        private Int16[] right_hor_offset = { 0, 0, 0 };

        private bool do_localize;
        private float filterweight;
        private const uint report_len = 49;

        private struct Rumble {
            private float h_f, l_f;
            public float t, amp, fullamp;
            public bool timed_rumble;

            public void set_vals(float low_freq, float high_freq, float amplitude, int time = 0) {
                h_f = high_freq;
                amp = amplitude;
                fullamp = amplitude;
                l_f = low_freq;
                timed_rumble = false;
                t = 0;
                if (time != 0) {
                    t = time / 1000f;
                    timed_rumble = true;
                }
            }
            public Rumble(float low_freq, float high_freq, float amplitude, int time = 0) {
                h_f = high_freq;
                amp = amplitude;
                fullamp = amplitude;
                l_f = low_freq;
                timed_rumble = false;
                t = 0;
                if (time != 0) {
                    t = time / 1000f;
                    timed_rumble = true;
                }
            }
            private float clamp(float x, float min, float max) {
                if (x < min) return min;
                if (x > max) return max;
                return x;
            }
            public byte[] GetData() {
                byte[] rumble_data = new byte[8];
                if (amp == 0.0f) {
                    rumble_data[0] = 0x0;
                    rumble_data[1] = 0x1;
                    rumble_data[2] = 0x40;
                    rumble_data[3] = 0x40;
                } else {
                    l_f = clamp(l_f, 40.875885f, 626.286133f);
                    amp = clamp(amp, 0.0f, 1.0f);
                    h_f = clamp(h_f, 81.75177f, 1252.572266f);
                    UInt16 hf = (UInt16)((Math.Round(32f * Math.Log(h_f * 0.1f, 2)) - 0x60) * 4);
                    byte lf = (byte)(Math.Round(32f * Math.Log(l_f * 0.1f, 2)) - 0x40);
                    byte hf_amp;
                    if (amp == 0) hf_amp = 0;
                    else if (amp < 0.117) hf_amp = (byte)(((Math.Log(amp * 1000, 2) * 32) - 0x60) / (5 - Math.Pow(amp, 2)) - 1);
                    else if (amp < 0.23) hf_amp = (byte)(((Math.Log(amp * 1000, 2) * 32) - 0x60) - 0x5c);
                    else hf_amp = (byte)((((Math.Log(amp * 1000, 2) * 32) - 0x60) * 2) - 0xf6);

                    UInt16 lf_amp = (UInt16)(Math.Round((double)hf_amp) * .5);
                    byte parity = (byte)(lf_amp % 2);
                    if (parity > 0) {
                        --lf_amp;
                    }

                    lf_amp = (UInt16)(lf_amp >> 1);
                    lf_amp += 0x40;
                    if (parity > 0) lf_amp |= 0x8000;
                    rumble_data = new byte[8];
                    rumble_data[0] = (byte)(hf & 0xff);
                    rumble_data[1] = (byte)((hf >> 8) & 0xff);
                    rumble_data[2] = lf;
                    rumble_data[1] += hf_amp;
                    rumble_data[2] += (byte)((lf_amp >> 8) & 0xff);
                    rumble_data[3] += (byte)(lf_amp & 0xff);
                }
                for (int i = 0; i < 4; ++i) {
                    rumble_data[4 + i] = rumble_data[i];
                }

                return rumble_data;
            }
        }

        private Rumble rumble_obj;

        private byte global_count = 0;
        private string debug_str;

        // For UdpServer
        public int PadId = 0;
        public int battery = 4;
        public int batteryLevel = 8;  // 0-8 from the controller
        public bool charging;
        public int batteryPercent = -1;

        // Subcommands requested from other threads (UI, tray, Xbox LED feedback); sent by the poll thread.
        readonly System.Collections.Concurrent.ConcurrentQueue<(byte sc, byte[] data)> pending = new System.Collections.Concurrent.ConcurrentQueue<(byte, byte[])>();
        DateTime lastActivity = DateTime.Now;
        readonly bool[] lastButtons = new bool[20];
        readonly float[] lastSticks = new float[4];
        public bool poweringOff;
        public int playerSlot = -1;  // estimated from battery voltage; -1 until the first reading
        DateTime nextVoltageRequest = DateTime.Now.AddSeconds(2);
        public int model = 2;
        public int constate = 2;
        public int connection = 3;

        public PhysicalAddress PadMacAddress = new PhysicalAddress(new byte[] { 01, 02, 03, 04, 05, 06 });
        public ulong Timestamp = 0;
        public int packetCounter = 0;

        public VirtualPad xin; // assigned by JoyconManager; may be shared across reconnects of the same controller
        public Xbox360Report report = new Xbox360Report();

        int rumblePeriod = Int32.Parse(ConfigurationManager.AppSettings["RumblePeriod"]);
        int lowFreq = Int32.Parse(ConfigurationManager.AppSettings["LowFreqRumble"]);
        int highFreq = Int32.Parse(ConfigurationManager.AppSettings["HighFreqRumble"]);

        bool toRumble = Boolean.Parse(ConfigurationManager.AppSettings["EnableRumble"]);

        bool showAsXInput = Boolean.Parse(ConfigurationManager.AppSettings["ShowAsXInput"]);

        public MainForm form;

        public byte LED = 0x0;

		public Joycon(IntPtr handle_, bool imu, bool localize, float alpha, bool left, string path, int id = 0, bool isPro=false, bool usb = false) {
			handle = handle_;
			imu_enabled = imu;
			do_localize = localize;
			rumble_obj = new Rumble(160, 320, 0);
			filterweight = alpha;
			isLeft = left;

			PadId = id;
			this.isPro = isPro;
			isUSB = usb;

            this.path = path;

			connection = isUSB ? 0x01 : 0x02;
		}

		// Creates a standalone virtual pad for this connection (used for Joy-Cons; Pro Controllers get theirs from JoyconManager).
		public void CreateOwnPad() {
			if (!showAsXInput) return;
			xin = new VirtualPad(Program.emClient);
			xin.Owner = this;
			if (toRumble)
				xin.Rumble += ReceiveRumble;
			xin.Slot += SetPlayerSlot;
		}

		public void ReceiveRumble(byte large, byte small) {
			if (state <= state_.ATTACHED) return;
			SetRumble(lowFreq, highFreq, (float)large / 255f, rumblePeriod);

			if (other != null)
				other.SetRumble(lowFreq, highFreq, (float)large / 255f, rumblePeriod);
		}

		public void DebugPrint(String s, DebugType d) {
			if (debug_type == DebugType.NONE) return;
			if (d == DebugType.ALL || d == debug_type || debug_type == DebugType.ALL) {
				form.AppendTextBox(s + "\r\n");
			}
		}
		public bool GetButtonDown(Button b) {
			return buttons_down[(int)b];
		}
		public bool GetButton(Button b) {
			return buttons[(int)b];
		}
		public bool GetButtonUp(Button b) {
			return buttons_up[(int)b];
		}
		public float[] GetStick() {
			return stick;
		}
		public float[] GetStick2() {
			return stick2;
		}
		public Vector3 GetGyro() {
			return gyr_g;
		}
		public Vector3 GetAccel() {
			return acc_g;
		}
		public int Attach(byte leds_ = 0x0) {
			state = state_.ATTACHED;

			// Make sure command is received
			HIDapi.hid_set_nonblocking(handle, 0);

			byte[] a = { 0x0 };

			// Connect
			if (!isUSB) {
				// Input report mode
				Subcommand(0x03, new byte[] { 0x30 }, 1, false);
			} else {
				form.AppendTextBox("Using USB.\r\n");
				if (!UsbHandshake())
					form.AppendTextBox("USB handshake got no reply. If Steam is open, turn off its Switch controller support or enable HidHide in the config.\r\n");
			}

			if (mac != null) {
				byte[] m = new byte[6];
				for (int n = 0; n < 6; n++)
					m[n] = Convert.ToByte(mac.Substring(n * 2, 2), 16);
				PadMacAddress = new PhysicalAddress(m);
			}

			dump_calibration_data();

            ApplyHomeLight();

            a[0] = leds_;
			Subcommand(0x30, a, 1);
			Subcommand(0x40, new byte[] { (imu_enabled ? (byte)0x1 : (byte)0x0) }, 1, true);
			Subcommand(0x3, new byte[] { 0x30 }, 1, true);
			Subcommand(0x48, new byte[] { 0x01 }, 1, true);
			
			Subcommand(0x41, new byte[] { 0x03, 0x00, 0x00, 0x01 }, 4, false); // higher gyro performance rate

			DebugPrint("Done with init.", DebugType.COMMS);

			HIDapi.hid_set_nonblocking(handle, 1);

			return 0;
		}

		// Sends a 0x80 USB command. When a reply is expected, waits for the matching 0x81 report and returns it (null on timeout).
		private byte[] UsbCommand(byte cmd, bool expectReply) {
			byte[] buf = new byte[64];
			buf[0] = 0x80; buf[1] = cmd;
			if (HIDapi.hid_write(handle, buf, new UIntPtr(64)) < 0) return null;
			if (!expectReply) return buf;

			byte[] resp = new byte[64];
			Stopwatch sw = Stopwatch.StartNew();
			while (sw.ElapsedMilliseconds < 300) {
				int r = HIDapi.hid_read_timeout(handle, resp, new UIntPtr(64), 50);
				if (r < 0) return null;
				if (r > 1 && resp[0] == 0x81 && resp[1] == cmd) return resp;
			}
			return null;
		}

		// USB start-up sequence (same as the Switch and SDL use): status, handshake, 3Mbit, handshake, force USB.
		// Each step waits for its reply; v5 fired them blind, which left the controller half-initialised.
		private bool UsbHandshake() {
			for (int attempt = 0; attempt < 3; attempt++) {
				if (attempt > 0) {
					UsbCommand(0x05, false); // controller may still be in "USB only" mode from an earlier session; release it and retry
					Thread.Sleep(100);
				}

				byte[] status = UsbCommand(0x01, true);
				if (status == null) continue;
				if (status[2] == 0x00) // byte 2 is 0 when a MAC follows (bytes 4-9, little endian)
					mac = string.Format("{0:X2}{1:X2}{2:X2}{3:X2}{4:X2}{5:X2}", status[9], status[8], status[7], status[6], status[5], status[4]);

				if (UsbCommand(0x02, true) == null) continue; // handshake
				if (UsbCommand(0x03, true) != null)            // 3Mbit baud rate
					UsbCommand(0x02, true);                    // handshake again at the new rate
				UsbCommand(0x04, false);                       // only talk over USB, no timeout (no reply)
				return true;
			}
			return false;
		}

		// Stops this connection without telling the controller anything, because the same controller
		// has come back on another connection (e.g. it was plugged in while on Bluetooth).
		public void Retire() {
			retired = true;
			StopPolling();
			if (handle != IntPtr.Zero) {
				HIDapi.hid_close(handle);
				handle = IntPtr.Zero;
			}
			xin = null;
			state = state_.NOT_ATTACHED;
		}

		private void StopPolling() {
			stop_polling = true;
			if (PollThreadObj != null && PollThreadObj != Thread.CurrentThread)
				PollThreadObj.Join(1000);
		}

        public void Queue(byte sc, params byte[] data) => pending.Enqueue((sc, data));

        // Puts the controller to sleep (Bluetooth). Press any button to wake it again.
        public void PowerOff() {
            if (isUSB || poweringOff) return;
            poweringOff = true;
            form.AppendTextBox((isPro ? "Pro Controller" : "Joy-Con") + " turned off.\r\n");
            Queue(0x06, 0x00);          // HCI state: disconnect / sleep
            other?.PowerOff();
        }

        // Player lights show which Xbox controller slot (1-4) this controller has.
        public void SetPlayerSlot(int slot) {
            if (slot < 0 || slot > 3) return;
            playerSlot = slot;
            if (Prefs.PlayerLights) Queue(0x30, (byte)(1 << slot));
            if (other != null && other.playerSlot != slot) other.SetPlayerSlot(slot);
        }

        // Home button ring (Pro Controller and right Joy-Con): steady glow at the chosen brightness.
        public void ApplyHomeLight() {
            if (!(isPro || !isLeft)) return;
            byte level = Prefs.HomeLight == "bright" ? (byte)0x0F : Prefs.HomeLight == "dim" ? (byte)0x04 : (byte)0x00;
            if (level == 0) Queue(0x38, 0x00, 0x00, 0x00, 0x00);
            else Queue(0x38, 0x01, (byte)(level << 4), (byte)(level << 4), 0x00); // one cycle, then stay at this level
        }

        // Called after each input report: any button change or real stick movement counts as activity.
        void TrackActivity() {
            bool changed = false;
            for (int i = 0; i < buttons.Length; i++)
                if (buttons[i] != lastButtons[i]) { lastButtons[i] = buttons[i]; changed = true; }
            float[] st = { stick[0], stick[1], stick2[0], stick2[1] };
            for (int i = 0; i < 4; i++)
                if (Math.Abs(st[i] - lastSticks[i]) > 0.15f) { lastSticks[i] = st[i]; changed = true; }
            if (changed) lastActivity = DateTime.Now;
        }

        public void SetLED(byte leds_ = 0x0) {
            Subcommand(0x30, new byte[] { leds_ }, 1);
        }

        public void BlinkLED() { // do not call after initial setup
            byte[] a = Enumerable.Repeat((byte)0xFF, 25).ToArray(); // LED ring
            a[0] = 0x18;
            a[1] = 0x01;
            Subcommand(0x38, a, 25, false);
        }

		public void SetFilterCoeff(float a) {
			filterweight = a;
		}

		// Virtual pads are released by JoyconManager, not here, so a reconnect can keep the same pad.
		public void Detach() {
			StopPolling();
			if (handle == IntPtr.Zero) { state = state_.NOT_ATTACHED; return; }

			if (state > state_.NO_JOYCONS) {
				HIDapi.hid_set_nonblocking(handle, 0);

				Subcommand(0x40, new byte[] { 0x0 }, 1);

				if (isUSB)
					UsbCommand(0x05, false); // let the controller time out of USB mode / go back to Bluetooth
			}
			HIDapi.hid_close(handle);
			handle = IntPtr.Zero;
			state = state_.NOT_ATTACHED;
		}

		private byte ts_en;
		private int ReceiveRaw() {
            if (handle == IntPtr.Zero) return -2;
            HIDapi.hid_set_nonblocking(handle, 0);
			byte[] raw_buf = new byte[report_len];
			// Timed read: a blocking read never returns if a Bluetooth controller goes quiet, so it was never marked dropped.
            int ret = HIDapi.hid_read_timeout(handle, raw_buf, new UIntPtr(report_len), 100);
			// Only 0x30 reports carry input. v5 also fed subcommand replies (0x21) and USB replies (0x81)
			// into the button parser, which caused random presses and stick jumps.
			// Reply to the battery-voltage request (subcommand 0x50): 2 bytes, units of 2.5 mV.
			if (ret > 0 && raw_buf[0] == 0x21 && raw_buf[14] == 0x50) {
				int units = raw_buf[15] | (raw_buf[16] << 8);
				if (units > 0) batteryPercent = PercentFromVoltage(units * 2.5f / 1000f);
			}
			if (ret > 0 && raw_buf[0] == 0x30) {
				// Byte 2, high nibble: battery level (8 full .. 0 empty), lowest bit = charging.
				batteryLevel = (raw_buf[2] >> 4) & 0x0E;
				charging = ((raw_buf[2] >> 4) & 0x01) != 0;
                // Process packets as soon as they come
                for (int n = 0; n < 3; n++) {
					ExtractIMUValues(raw_buf, n);
					GyroSample();

					byte lag = (byte) Math.Max(0, raw_buf[1] - ts_en - 3);
					if (n == 0) {
						Timestamp += (ulong)lag * 5000; // add lag once
						ProcessButtonsAndStick(raw_buf);
						Shortcuts.Process(this);
						TrackActivity();
					}
					Timestamp += 5000; // 5ms difference

					packetCounter++;
					if (Program.server != null)
						Program.server.NewReportIncoming(this);

					if (xin != null)
						xin.SendReport(report);
				}

				GyroMouse.Flush(this);

				if (ts_en == raw_buf[1]) {
					form.AppendTextBox("Duplicate timestamp enqueued.\r\n");
					DebugPrint(string.Format("Duplicate timestamp enqueued. TS: {0:X2}", ts_en), DebugType.THREADING);
				}
				ts_en = raw_buf[1];
				DebugPrint(string.Format("Enqueue. Bytes read: {0:D}. Timestamp: {1:X2}", ret, raw_buf[1]), DebugType.THREADING);
			}
			return ret;
		}

		private Thread PollThreadObj; // pro times out over time randomly if it was USB and then bluetooth??
		private void Poll() {
			Stopwatch sinceData = Stopwatch.StartNew();
			int dropAfterMs = isUSB ? 1500 : 3000;
			while (!stop_polling & state > state_.NO_JOYCONS) {
                SendRumble(rumble_obj.GetData()); // Needed for EVERYTHING to not time out. Never remove pls
                (byte sc, byte[] data) cmd;
                if (pending.TryDequeue(out cmd))
                    SendSubcommandNoWait(cmd.sc, cmd.data);
                if (!isUSB && !poweringOff && Prefs.AutoSleepMinutes > 0 && (DateTime.Now - lastActivity).TotalMinutes >= Prefs.AutoSleepMinutes) {
                    form.AppendTextBox("No input for " + Prefs.AutoSleepMinutes + " minutes - ");
                    PowerOff();
                }
                if (DateTime.Now >= nextVoltageRequest) {
                    nextVoltageRequest = DateTime.Now.AddSeconds(20);
                    SendSubcommandNoWait(0x50, new byte[0]); // ask for battery voltage; the reply is parsed in ReceiveRaw
                }
                int a = ReceiveRaw();

                if (a > 0) {
                    state = state_.IMU_DATA_OK;
					sinceData.Restart();
				} else if (sinceData.ElapsedMilliseconds > dropAfterMs) {
					if (stop_polling) break;
					state = state_.DROPPED;
                    form.AppendTextBox((isUSB ? "USB" : "Bluetooth") + " connection dropped.\r\n");
					break;
				} else if (a < 0) {
					Thread.Sleep(5); // read error (e.g. unplugged): don't spin
				}
            }
		}

		public void Update() {
            if (state > state_.NO_JOYCONS) {	
				if (rumble_obj.timed_rumble) {
					if (rumble_obj.t < 0) {
						rumble_obj.set_vals(lowFreq, highFreq, 0, 0);
					} else {
						rumble_obj.t -= (1 / timing);
                        //rumble_obj.amp = (float) Math.Sin(((timing - rumble_obj.t * 1000f) / timing) * Math.PI) * rumble_obj.fullamp;
					}
				}
			}
		}

		public float[] otherStick = { 0, 0 };

		bool swapButtons = Boolean.Parse(ConfigurationManager.AppSettings["SwapButtons"]);
		private int ProcessButtonsAndStick(byte[] report_buf) {
			if (report_buf[0] == 0x00) return -1;

			stick_raw[0] = report_buf[6 + (isLeft ? 0 : 3)];
			stick_raw[1] = report_buf[7 + (isLeft ? 0 : 3)];
			stick_raw[2] = report_buf[8 + (isLeft ? 0 : 3)];

			if (isPro) {
				stick2_raw[0] = report_buf[6 + (!isLeft ? 0 : 3)];
				stick2_raw[1] = report_buf[7 + (!isLeft ? 0 : 3)];
				stick2_raw[2] = report_buf[8 + (!isLeft ? 0 : 3)];
			}

			stick_precal[0] = (UInt16)(stick_raw[0] | ((stick_raw[1] & 0xf) << 8));
			stick_precal[1] = (UInt16)((stick_raw[1] >> 4) | (stick_raw[2] << 4));

			stick = CenterSticks(stick_precal, stick_cal, deadzone);

			if (isPro) {
				stick2_precal[0] = (UInt16)(stick2_raw[0] | ((stick2_raw[1] & 0xf) << 8));
				stick2_precal[1] = (UInt16)((stick2_raw[1] >> 4) | (stick2_raw[2] << 4));
				stick2 = CenterSticks(stick2_precal, stick2_cal, deadzone2);
			}

			// Read other Joycon's sticks
			if (isLeft && other != null) {
				stick2 = otherStick;
				other.otherStick = stick;
			}

			if (!isLeft && other != null) {
				Array.Copy(stick, stick2, 2);
				stick = otherStick;
				other.otherStick = stick2;
			}
			//

			lock (buttons) {
				lock (down_) {
					for (int i = 0; i < buttons.Length; ++i) {
						down_[i] = buttons[i];
					}
				}
				buttons[(int)Button.DPAD_DOWN] = (report_buf[3 + (isLeft ? 2 : 0)] & (isLeft ? 0x01 : 0x04)) != 0;
				buttons[(int)Button.DPAD_RIGHT] = (report_buf[3 + (isLeft ? 2 : 0)] & (isLeft ? 0x04 : 0x08)) != 0;
				buttons[(int)Button.DPAD_UP] = (report_buf[3 + (isLeft ? 2 : 0)] & (isLeft ? 0x02 : 0x02)) != 0;
				buttons[(int)Button.DPAD_LEFT] = (report_buf[3 + (isLeft ? 2 : 0)] & (isLeft ? 0x08 : 0x01)) != 0;
				buttons[(int)Button.HOME] = ((report_buf[4] & 0x10) != 0);
                buttons[(int)Button.CAPTURE] = ((report_buf[4] & 0x20) != 0);
				buttons[(int)Button.MINUS] = ((report_buf[4] & 0x01) != 0);
				buttons[(int)Button.PLUS] = ((report_buf[4] & 0x02) != 0);
				buttons[(int)Button.STICK] = ((report_buf[4] & (isLeft ? 0x08 : 0x04)) != 0);
				buttons[(int)Button.SHOULDER_1] = (report_buf[3 + (isLeft ? 2 : 0)] & 0x40) != 0;
				buttons[(int)Button.SHOULDER_2] = (report_buf[3 + (isLeft ? 2 : 0)] & 0x80) != 0;
				buttons[(int)Button.SR] = (report_buf[3 + (isLeft ? 2 : 0)] & 0x10) != 0;
				buttons[(int)Button.SL] = (report_buf[3 + (isLeft ? 2 : 0)] & 0x20) != 0;

				if (isPro && xin != null) {
					buttons[(int)Button.B] = (report_buf[3 + (!isLeft ? 2 : 0)] & (!isLeft ? 0x01 : 0x04)) != 0;
					buttons[(int)Button.A] = (report_buf[3 + (!isLeft ? 2 : 0)] & (!isLeft ? 0x04 : 0x08)) != 0;
					buttons[(int)Button.X] = (report_buf[3 + (!isLeft ? 2 : 0)] & (!isLeft ? 0x02 : 0x02)) != 0;
					buttons[(int)Button.Y] = (report_buf[3 + (!isLeft ? 2 : 0)] & (!isLeft ? 0x08 : 0x01)) != 0;

					buttons[(int)Button.STICK2] = ((report_buf[4] & (!isLeft ? 0x08 : 0x04)) != 0);
					buttons[(int)Button.SHOULDER2_1] = (report_buf[3 + (!isLeft ? 2 : 0)] & 0x40) != 0;
					buttons[(int)Button.SHOULDER2_2] = (report_buf[3 + (!isLeft ? 2 : 0)] & 0x80) != 0;

					report.SetButtonState(Xbox360Buttons.A, buttons[(int)(!swapButtons ? Button.B : Button.A)]);
					report.SetButtonState(Xbox360Buttons.B, buttons[(int)(!swapButtons ? Button.A : Button.B)]);
					report.SetButtonState(Xbox360Buttons.Y, buttons[(int)(!swapButtons ? Button.X : Button.Y)]);
					report.SetButtonState(Xbox360Buttons.X, buttons[(int)(!swapButtons ? Button.Y : Button.X)]);
					report.SetButtonState(Xbox360Buttons.Up, buttons[(int)Button.DPAD_UP]);
					report.SetButtonState(Xbox360Buttons.Down, buttons[(int)Button.DPAD_DOWN]);
					report.SetButtonState(Xbox360Buttons.Left, buttons[(int)Button.DPAD_LEFT]);
					report.SetButtonState(Xbox360Buttons.Right, buttons[(int)Button.DPAD_RIGHT]);
					report.SetButtonState(Xbox360Buttons.Back, buttons[(int)Button.MINUS]);
					report.SetButtonState(Xbox360Buttons.Start, buttons[(int)Button.PLUS]);
					report.SetButtonState(Xbox360Buttons.Guide, buttons[(int)Button.HOME]);
					report.SetButtonState(Xbox360Buttons.LeftShoulder, buttons[(int)Button.SHOULDER_1]);
					report.SetButtonState(Xbox360Buttons.RightShoulder, buttons[(int)Button.SHOULDER2_1]);
					report.SetButtonState(Xbox360Buttons.LeftThumb, buttons[(int)Button.STICK]);
					report.SetButtonState(Xbox360Buttons.RightThumb, buttons[(int)Button.STICK2]);
				}

				if (other != null) {
					buttons[(int)(Button.B)] = other.buttons[(int)Button.DPAD_DOWN];
					buttons[(int)(Button.A)] = other.buttons[(int)Button.DPAD_RIGHT];
					buttons[(int)(Button.X)] = other.buttons[(int)Button.DPAD_UP];
					buttons[(int)(Button.Y)] = other.buttons[(int)Button.DPAD_LEFT];

					buttons[(int)Button.STICK2] = other.buttons[(int)Button.STICK];
					buttons[(int)Button.SHOULDER2_1] = other.buttons[(int)Button.SHOULDER_1];
					buttons[(int)Button.SHOULDER2_2] = other.buttons[(int)Button.SHOULDER_2];
				}

				if (isLeft && other != null) {
					buttons[(int)Button.HOME] = other.buttons[(int)Button.HOME];
					buttons[(int)Button.PLUS] = other.buttons[(int)Button.PLUS];
				}

				if (!isLeft && other != null) {
					buttons[(int)Button.MINUS] = other.buttons[(int)Button.MINUS];
				}

				if (!isPro && xin != null) {
                    if (other != null) {
                        report.SetButtonState(!swapButtons ? Xbox360Buttons.A : Xbox360Buttons.B, buttons[(int)(isLeft ? Button.B : Button.DPAD_DOWN)]);
                        report.SetButtonState(!swapButtons ? Xbox360Buttons.B : Xbox360Buttons.A, buttons[(int)(isLeft ? Button.A : Button.DPAD_RIGHT)]);
                        report.SetButtonState(!swapButtons ? Xbox360Buttons.Y : Xbox360Buttons.X, buttons[(int)(isLeft ? Button.X : Button.DPAD_UP)]);
                        report.SetButtonState(!swapButtons ? Xbox360Buttons.X : Xbox360Buttons.Y, buttons[(int)(isLeft ? Button.Y : Button.DPAD_LEFT)]);
                        report.SetButtonState(Xbox360Buttons.Up, buttons[(int)(isLeft ? Button.DPAD_UP : Button.X)]);
                        report.SetButtonState(Xbox360Buttons.Down, buttons[(int)(isLeft ? Button.DPAD_DOWN : Button.B)]);
                        report.SetButtonState(Xbox360Buttons.Left, buttons[(int)(isLeft ? Button.DPAD_LEFT : Button.Y)]);
                        report.SetButtonState(Xbox360Buttons.Right, buttons[(int)(isLeft ? Button.DPAD_RIGHT : Button.A)]);
                        report.SetButtonState(Xbox360Buttons.Back, buttons[(int)Button.MINUS]);
                        report.SetButtonState(Xbox360Buttons.Start, buttons[(int)Button.PLUS]);
                        report.SetButtonState(Xbox360Buttons.Guide, buttons[(int)Button.HOME]);
                        report.SetButtonState(Xbox360Buttons.LeftShoulder, buttons[(int)(isLeft ? Button.SHOULDER_1 : Button.SHOULDER2_1)]);
                        report.SetButtonState(Xbox360Buttons.RightShoulder, buttons[(int)(isLeft ? Button.SHOULDER2_1 : Button.SHOULDER_1)]);
                        report.SetButtonState(Xbox360Buttons.LeftThumb, buttons[(int)(isLeft ? Button.STICK : Button.STICK2)]);
                        report.SetButtonState(Xbox360Buttons.RightThumb, buttons[(int)(isLeft ? Button.STICK2 : Button.STICK)]);
                    } else { // single joycon mode
                        report.SetButtonState(!swapButtons ? Xbox360Buttons.A : Xbox360Buttons.B, buttons[(int)(isLeft ? Button.DPAD_LEFT : Button.DPAD_RIGHT)]);
                        report.SetButtonState(!swapButtons ? Xbox360Buttons.B : Xbox360Buttons.A, buttons[(int)(isLeft ? Button.DPAD_DOWN : Button.DPAD_UP)]);
                        report.SetButtonState(!swapButtons ? Xbox360Buttons.Y : Xbox360Buttons.X, buttons[(int)(isLeft ? Button.DPAD_RIGHT : Button.DPAD_LEFT)]);
                        report.SetButtonState(!swapButtons ? Xbox360Buttons.X : Xbox360Buttons.Y, buttons[(int)(isLeft ? Button.DPAD_UP : Button.DPAD_DOWN)]);
                        report.SetButtonState(Xbox360Buttons.Back, buttons[(int)Button.MINUS] | buttons[(int)Button.HOME]);
                        report.SetButtonState(Xbox360Buttons.Start, buttons[(int)Button.PLUS] | buttons[(int)Button.CAPTURE]);

                        report.SetButtonState(Xbox360Buttons.LeftShoulder, buttons[(int)Button.SL]);
                        report.SetButtonState(Xbox360Buttons.RightShoulder, buttons[(int)Button.SR]);

                        report.SetButtonState(Xbox360Buttons.LeftThumb, buttons[(int)Button.STICK]);
                    }
				}

				lock (buttons_up) {
					lock (buttons_down) {
						for (int i = 0; i < buttons.Length; ++i) {
							buttons_up[i] = (down_[i] & !buttons[i]);
							buttons_down[i] = (!down_[i] & buttons[i]);
						}
					}
				}
			}

			if (xin != null) {
                if (other != null | isPro) {
                    report.SetAxis(Xbox360Axes.LeftThumbX, (short)Math.Max(Int16.MinValue, Math.Min(Int16.MaxValue, stick[0] * (stick[0] > 0 ? Int16.MaxValue : -Int16.MinValue))));
                    report.SetAxis(Xbox360Axes.LeftThumbY, (short)Math.Max(Int16.MinValue, Math.Min(Int16.MaxValue, stick[1] * (stick[1] > 0 ? Int16.MaxValue : -Int16.MinValue))));
                    report.SetAxis(Xbox360Axes.RightThumbX, (short)Math.Max(Int16.MinValue, Math.Min(Int16.MaxValue, stick2[0] * (stick2[0] > 0 ? Int16.MaxValue : -Int16.MinValue))));
                    report.SetAxis(Xbox360Axes.RightThumbY, (short)Math.Max(Int16.MinValue, Math.Min(Int16.MaxValue, stick2[1] * (stick2[1] > 0 ? Int16.MaxValue : -Int16.MinValue))));
                } else { // single joycon mode
                    report.SetAxis(Xbox360Axes.LeftThumbY, (short)((isLeft ? 1 : -1) * Math.Max(Int16.MinValue, Math.Min(Int16.MaxValue, stick[0] * (stick[0] > 0 ? Int16.MaxValue : -Int16.MinValue)))));
                    report.SetAxis(Xbox360Axes.LeftThumbX, (short)((isLeft ? -1 : 1) * Math.Max(Int16.MinValue, Math.Min(Int16.MaxValue, stick[1] * (stick[1] > 0 ? Int16.MaxValue : -Int16.MinValue)))));
                }
                report.SetAxis(Xbox360Axes.LeftTrigger, (short)(buttons[(int)(isLeft ? Button.SHOULDER_2 : Button.SHOULDER2_2)] ? Int16.MaxValue : 0));
				report.SetAxis(Xbox360Axes.RightTrigger, (short)(buttons[(int)(isLeft ? Button.SHOULDER2_2 : Button.SHOULDER_2)] ? Int16.MaxValue : 0));
			}

			return 0;
		}
		// Gyro rates in degrees/second for aiming (same axes as the newer BetterJoy fork): yaw = turn, pitch = tilt.
		void GyroSample() {
			if (Prefs.GyroMode == "off" || !GyroMouse.IsAimer(this)) return;
			float dir = isLeft ? 1 : -1;
			float pitch = -dir * (gyr_r[1] - gyr_neutral[1]) * (816.0f / (gyr_sensiti[1] - gyr_neutral[1]));
			float yaw = -dir * (gyr_r[2] - gyr_neutral[2]) * (816.0f / (gyr_sensiti[2] - gyr_neutral[2]));
			if (float.IsNaN(yaw) || float.IsInfinity(yaw) || float.IsNaN(pitch) || float.IsInfinity(pitch)) return;
			GyroMouse.Sample(this, yaw, pitch, 0.005f);
		}

		private void ExtractIMUValues(byte[] report_buf, int n = 0) {
			gyr_r[0] = (Int16)(report_buf[19 + n * 12] | ((report_buf[20 + n * 12] << 8) & 0xff00));
			gyr_r[1] = (Int16)(report_buf[21 + n * 12] | ((report_buf[22 + n * 12] << 8) & 0xff00));
			gyr_r[2] = (Int16)(report_buf[23 + n * 12] | ((report_buf[24 + n * 12] << 8) & 0xff00));
			acc_r[0] = (Int16)(report_buf[13 + n * 12] | ((report_buf[14 + n * 12] << 8) & 0xff00));
			acc_r[1] = (Int16)(report_buf[15 + n * 12] | ((report_buf[16 + n * 12] << 8) & 0xff00));
			acc_r[2] = (Int16)(report_buf[17 + n * 12] | ((report_buf[18 + n * 12] << 8) & 0xff00));

			Int16[] offset;
			if (isPro)
				offset = pro_hor_offset;
			else if (isLeft)
				offset = left_hor_offset;
			else
				offset = right_hor_offset;

			for (int i = 0; i < 3; ++i) {
				switch (i) {
					case 0:
						acc_g.X = (acc_r[i] - offset[i]) * (1.0f / (acc_sensiti[i] - acc_neutral[i])) * 4.0f;
						gyr_g.X = (gyr_r[i] - gyr_neutral[i]) * (816.0f / (gyr_sensiti[i] - gyr_neutral[i]));

						break;
					case 1:
						acc_g.Y = (!isLeft ? -1 : 1) * (acc_r[i] - offset[i]) * (1.0f / (acc_sensiti[i] - acc_neutral[i])) * 4.0f;
						gyr_g.Y = -(!isLeft ? -1 : 1) * (gyr_r[i] - gyr_neutral[i] * 0.5f) * (816.0f / (gyr_sensiti[i] - gyr_neutral[i]));

						break;
					case 2:
						acc_g.Z = (!isLeft ? -1 : 1) * (acc_r[i] - offset[i]) * (1.0f / (acc_sensiti[i] - acc_neutral[i])) * 4.0f;
						gyr_g.Z = -(!isLeft ? -1 : 1) * (gyr_r[i] - gyr_neutral[i] * 0.5f) * (816.0f / (gyr_sensiti[i] - gyr_neutral[i]));

						break;
				}
			}

            if (other == null && !isPro) { // single joycon mode; Z do not swap, rest do
                if (isLeft) {
                    acc_g.X = -acc_g.X;
                    gyr_g.X = -gyr_g.X;
                } else {
                    gyr_g.Y = -gyr_g.Y;
                }
                
                float temp = acc_g.X; acc_g.X = acc_g.Y; acc_g.Y = temp;
                temp = gyr_g.X; gyr_g.X = gyr_g.Y; gyr_g.Y = temp;
            }
		}

		public void Begin() {
			if (PollThreadObj == null) {
				PollThreadObj = new Thread(new ThreadStart(Poll));
                PollThreadObj.IsBackground = true;
				PollThreadObj.Start();

				form.AppendTextBox("Starting poll thread.\r\n");
			} else {
                form.AppendTextBox("Poll cannot start.\r\n");
            }
		}

		public void Recenter() {
			first_imu_packet = true;
		}

		private float[] CenterSticks(UInt16[] vals, ushort[] cal, ushort dz) {
			ushort[] t = cal;

			float[] s = { 0, 0 };
			for (uint i = 0; i < 2; ++i) {
				float diff = vals[i] - t[2 + i];
				if (Math.Abs(diff) < dz) vals[i] = 0;
				else if (diff > 0) // if axis is above center
				{
					s[i] = diff / t[i];
				} else {
					s[i] = diff / t[4 + i];
				}
			}
			return s;
		}

		public void SetRumble(float low_freq, float high_freq, float amp, int time = 0) {
			if (state <= Joycon.state_.ATTACHED) return;
			//if (rumble_obj.timed_rumble == false || rumble_obj.t < 0) {
				rumble_obj = new Rumble(low_freq, high_freq, amp, time);
			//}
		}

		// Sends a subcommand from the poll thread without waiting; its 0x21 reply arrives with the normal input.
		private void SendSubcommandNoWait(byte sc, byte[] data) {
			byte[] buf_ = new byte[report_len];
			Array.Copy(rumble_obj.GetData(), 0, buf_, 2, 8);
			Array.Copy(data, 0, buf_, 11, data.Length);
			buf_[0] = 0x1;
			buf_[1] = global_count;
			buf_[10] = sc;
			if (global_count == 0xf) global_count = 0;
			else ++global_count;
			HIDapi.hid_write(handle, buf_, new UIntPtr((uint)(11 + data.Length)));
		}

		// Li-ion discharge curve for the controller's single cell (rough, +-10%).
		static readonly float[] CurveV = { 3.30f, 3.55f, 3.65f, 3.70f, 3.75f, 3.80f, 3.85f, 3.90f, 3.97f, 4.05f, 4.15f };
		static readonly int[] CurveP = { 0, 5, 15, 25, 40, 50, 60, 70, 80, 90, 100 };
		static int PercentFromVoltage(float v) {
			if (v <= CurveV[0]) return 0;
			for (int i = 1; i < CurveV.Length; i++)
				if (v <= CurveV[i])
					return (int)Math.Round(CurveP[i - 1] + (CurveP[i] - CurveP[i - 1]) * (v - CurveV[i - 1]) / (CurveV[i] - CurveV[i - 1]));
			return 100;
		}

		private void SendRumble(byte[] buf) {
			byte[] buf_ = new byte[report_len];
			buf_[0] = 0x10;
			buf_[1] = global_count;
			if (global_count == 0xf) global_count = 0;
			else ++global_count;
			Array.Copy(buf, 0, buf_, 2, 8);
			PrintArray(buf_, DebugType.RUMBLE, format: "Rumble data sent: {0:S}");
			HIDapi.hid_write(handle, buf_, new UIntPtr(report_len));
		}

		private byte[] Subcommand(byte sc, byte[] buf, uint len, bool print = true) {
			byte[] buf_ = new byte[report_len];
			byte[] response = new byte[report_len];
			Array.Copy(default_buf, 0, buf_, 2, 8);
			Array.Copy(buf, 0, buf_, 11, len);
			buf_[10] = sc;
			buf_[1] = global_count;
			buf_[0] = 0x1;
			if (global_count == 0xf) global_count = 0;
			else ++global_count;
			if (print) { PrintArray(buf_, DebugType.COMMS, len, 11, "Subcommand 0x" + string.Format("{0:X2}", sc) + " sent. Data: 0x{0:S}"); };
			HIDapi.hid_write(handle, buf_, new UIntPtr(len + 11));
			// Input reports keep streaming while we wait, so skip them until the 0x21 reply for this subcommand arrives.
			int res = 0;
			Stopwatch sw = Stopwatch.StartNew();
			while (sw.ElapsedMilliseconds < 100) {
				res = HIDapi.hid_read_timeout(handle, response, new UIntPtr(report_len), 20);
				if (res < 0) break;
				if (res > 0 && response[0] == 0x21 && response[14] == sc) break;
				res = 0;
			}
			if (res < 1) DebugPrint("No response.", DebugType.COMMS);
			else if (print) { PrintArray(response, DebugType.COMMS, report_len - 1, 1, "Response ID 0x" + string.Format("{0:X2}", response[0]) + ". Data: 0x{0:S}"); }
			return response;
		}

		private void dump_calibration_data() {
			byte[] buf_ = ReadSPI(0x80, (isLeft ? (byte)0x12 : (byte)0x1d), 9); // get user calibration data if possible
			bool found = false;
			for (int i = 0; i < 9; ++i) {
				if (buf_[i] != 0xff) {
					form.AppendTextBox("Using user stick calibration data.\r\n");
					found = true;
					break;
				}
			}
			if (!found) {
				form.AppendTextBox("Using factory stick calibration data.\r\n");
				buf_ = ReadSPI(0x60, (isLeft ? (byte)0x3d : (byte)0x46), 9); // get user calibration data if possible
			}
			stick_cal[isLeft ? 0 : 2] = (UInt16)((buf_[1] << 8) & 0xF00 | buf_[0]); // X Axis Max above center
			stick_cal[isLeft ? 1 : 3] = (UInt16)((buf_[2] << 4) | (buf_[1] >> 4));  // Y Axis Max above center
			stick_cal[isLeft ? 2 : 4] = (UInt16)((buf_[4] << 8) & 0xF00 | buf_[3]); // X Axis Center
			stick_cal[isLeft ? 3 : 5] = (UInt16)((buf_[5] << 4) | (buf_[4] >> 4));  // Y Axis Center
			stick_cal[isLeft ? 4 : 0] = (UInt16)((buf_[7] << 8) & 0xF00 | buf_[6]); // X Axis Min below center
			stick_cal[isLeft ? 5 : 1] = (UInt16)((buf_[8] << 4) | (buf_[7] >> 4));  // Y Axis Min below center

			PrintArray(stick_cal, len: 6, start: 0, format: "Stick calibration data: {0:S}");

			if (isPro) {
				buf_ = ReadSPI(0x80, (!isLeft ? (byte)0x12 : (byte)0x1d), 9); // get user calibration data if possible
				found = false;
				for (int i = 0; i < 9; ++i) {
					if (buf_[i] != 0xff) {
						form.AppendTextBox("Using user stick calibration data.\r\n");
						found = true;
						break;
					}
				}
				if (!found) {
					form.AppendTextBox("Using factory stick calibration data.\r\n");
					buf_ = ReadSPI(0x60, (!isLeft ? (byte)0x3d : (byte)0x46), 9); // get user calibration data if possible
				}
				stick2_cal[!isLeft ? 0 : 2] = (UInt16)((buf_[1] << 8) & 0xF00 | buf_[0]); // X Axis Max above center
				stick2_cal[!isLeft ? 1 : 3] = (UInt16)((buf_[2] << 4) | (buf_[1] >> 4));  // Y Axis Max above center
				stick2_cal[!isLeft ? 2 : 4] = (UInt16)((buf_[4] << 8) & 0xF00 | buf_[3]); // X Axis Center
				stick2_cal[!isLeft ? 3 : 5] = (UInt16)((buf_[5] << 4) | (buf_[4] >> 4));  // Y Axis Center
				stick2_cal[!isLeft ? 4 : 0] = (UInt16)((buf_[7] << 8) & 0xF00 | buf_[6]); // X Axis Min below center
				stick2_cal[!isLeft ? 5 : 1] = (UInt16)((buf_[8] << 4) | (buf_[7] >> 4));  // Y Axis Min below center

				PrintArray(stick2_cal, len: 6, start: 0, format: "Stick calibration data: {0:S}");

				buf_ = ReadSPI(0x60, (!isLeft ? (byte)0x86 : (byte)0x98), 16);
				deadzone2 = (UInt16)((buf_[4] << 8) & 0xF00 | buf_[3]);
			}

			buf_ = ReadSPI(0x60, (isLeft ? (byte)0x86 : (byte)0x98), 16);
			deadzone = (UInt16)((buf_[4] << 8) & 0xF00 | buf_[3]);

			buf_ = ReadSPI(0x80, 0x28, 10);
			acc_neutral[0] = (Int16)(buf_[0] | ((buf_[1] << 8) & 0xff00));
			acc_neutral[1] = (Int16)(buf_[2] | ((buf_[3] << 8) & 0xff00));
			acc_neutral[2] = (Int16)(buf_[4] | ((buf_[5] << 8) & 0xff00));

			buf_ = ReadSPI(0x80, 0x2E, 10);
			acc_sensiti[0] = (Int16)(buf_[0] | ((buf_[1] << 8) & 0xff00));
			acc_sensiti[1] = (Int16)(buf_[2] | ((buf_[3] << 8) & 0xff00));
			acc_sensiti[2] = (Int16)(buf_[4] | ((buf_[5] << 8) & 0xff00));

			buf_ = ReadSPI(0x80, 0x34, 10);
			gyr_neutral[0] = (Int16)(buf_[0] | ((buf_[1] << 8) & 0xff00));
			gyr_neutral[1] = (Int16)(buf_[2] | ((buf_[3] << 8) & 0xff00));
			gyr_neutral[2] = (Int16)(buf_[4] | ((buf_[5] << 8) & 0xff00));

			buf_ = ReadSPI(0x80, 0x3A, 10);
			gyr_sensiti[0] = (Int16)(buf_[0] | ((buf_[1] << 8) & 0xff00));
			gyr_sensiti[1] = (Int16)(buf_[2] | ((buf_[3] << 8) & 0xff00));
			gyr_sensiti[2] = (Int16)(buf_[4] | ((buf_[5] << 8) & 0xff00));

			PrintArray(gyr_neutral, len: 3, d: DebugType.IMU, format: "User gyro neutral position: {0:S}");

			// This is an extremely messy way of checking to see whether there is user stick calibration data present, but I've seen conflicting user calibration data on blank Joy-Cons. Worth another look eventually.
			if (gyr_neutral[0] + gyr_neutral[1] + gyr_neutral[2] == -3 || Math.Abs(gyr_neutral[0]) > 100 || Math.Abs(gyr_neutral[1]) > 100 || Math.Abs(gyr_neutral[2]) > 100) {
				buf_ = ReadSPI(0x60, 0x20, 10);
				acc_neutral[0] = (Int16)(buf_[0] | ((buf_[1] << 8) & 0xff00));
				acc_neutral[1] = (Int16)(buf_[2] | ((buf_[3] << 8) & 0xff00));
				acc_neutral[2] = (Int16)(buf_[4] | ((buf_[5] << 8) & 0xff00));

				buf_ = ReadSPI(0x60, 0x26, 10);
				acc_sensiti[0] = (Int16)(buf_[0] | ((buf_[1] << 8) & 0xff00));
				acc_sensiti[1] = (Int16)(buf_[2] | ((buf_[3] << 8) & 0xff00));
				acc_sensiti[2] = (Int16)(buf_[4] | ((buf_[5] << 8) & 0xff00));

				buf_ = ReadSPI(0x60, 0x2C, 10);
				gyr_neutral[0] = (Int16)(buf_[0] | ((buf_[1] << 8) & 0xff00));
				gyr_neutral[1] = (Int16)(buf_[2] | ((buf_[3] << 8) & 0xff00));
				gyr_neutral[2] = (Int16)(buf_[4] | ((buf_[5] << 8) & 0xff00));

				buf_ = ReadSPI(0x60, 0x32, 10);
				gyr_sensiti[0] = (Int16)(buf_[0] | ((buf_[1] << 8) & 0xff00));
				gyr_sensiti[1] = (Int16)(buf_[2] | ((buf_[3] << 8) & 0xff00));
				gyr_sensiti[2] = (Int16)(buf_[4] | ((buf_[5] << 8) & 0xff00));

				PrintArray(gyr_neutral, len: 3, d: DebugType.IMU, format: "Factory gyro neutral position: {0:S}");
			}
		}

		private byte[] ReadSPI(byte addr1, byte addr2, uint len, bool print = false) {
			byte[] buf = { addr2, addr1, 0x00, 0x00, (byte)len };
			byte[] read_buf = new byte[len];
			byte[] buf_ = new byte[len + 20];

			for (int i = 0; i < 10; ++i) {
				buf_ = Subcommand(0x10, buf, 5, false);
				if (buf_[15] == addr2 && buf_[16] == addr1) {
					break;
				}
			}
			Array.Copy(buf_, 20, read_buf, 0, len);
			if (print) PrintArray(read_buf, DebugType.COMMS, len);
			return read_buf;
		}

		private void PrintArray<T>(T[] arr, DebugType d = DebugType.NONE, uint len = 0, uint start = 0, string format = "{0:S}") {
			if (d != debug_type && debug_type != DebugType.ALL) return;
			if (len == 0) len = (uint)arr.Length;
			string tostr = "";
			for (int i = 0; i < len; ++i) {
				tostr += string.Format((arr[0] is byte) ? "{0:X2} " : ((arr[0] is float) ? "{0:F} " : "{0:D} "), arr[i + start]);
			}
			DebugPrint(string.Format(format, tostr), d);
		}
	}
}
