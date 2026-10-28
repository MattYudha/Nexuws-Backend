using Photon.Deterministic;
using Quantum;
using UnityEngine;
using QuantumInput = Quantum.Input;

namespace Nexus
{
    /// <summary>
    /// Keyboard/touch → Nexus input tiap tick (SPIKE v0).
    /// WASD/arrow = gerak 8 arah; Space/J = Attack; mirror pola PongInput.
    /// </summary>
    public unsafe class NexusInput : MonoBehaviour
    {
        /// <summary>Diset oleh UI Pilih Hero (NexusView.OnGUI): 0 belum, 1 TITAN-01, 2 PULSE-X.
        /// Dibaca ke input tiap tick; setelah match mulai (State=1) sim mengabaikannya.</summary>
        public static byte PendingPick = 0;

        private void OnEnable()
        {
            QuantumCallback.Subscribe(this, (CallbackPollInput cb) => Poll(cb));
        }

        private void Poll(CallbackPollInput callback)
        {
            var input = new QuantumInput
            {
                MoveDir  = ReadDir8(),
                Buttons  = ReadButtons(),
                AimYaw   = 0,
                PickHero = PendingPick,
            };
            callback.SetInput(input, DeterministicInputFlags.Repeatable);
        }

        private static byte ReadDir8()
        {
            float x = UnityEngine.Input.GetAxisRaw("Horizontal");
            float y = UnityEngine.Input.GetAxisRaw("Vertical");
            int ix = Mathf.Abs(x) > 0.3f ? (x > 0 ? 1 : -1) : 0;
            int iy = Mathf.Abs(y) > 0.3f ? (y > 0 ? 1 : -1) : 0;
            // Peta (ix,iy) → 1=N..8=NW (searah jarum jam), 0=netral.
            if (ix == 0 && iy == 1) return 1;
            if (ix == 1 && iy == 1) return 2;
            if (ix == 1 && iy == 0) return 3;
            if (ix == 1 && iy == -1) return 4;
            if (ix == 0 && iy == -1) return 5;
            if (ix == -1 && iy == -1) return 6;
            if (ix == -1 && iy == 0) return 7;
            if (ix == -1 && iy == 1) return 8;
            return 0;
        }

        private static byte ReadButtons()
        {
            byte b = 0;
            if (UnityEngine.Input.GetKey(KeyCode.J)) b |= 1;         // Attack / tembak (PULSE)
            if (UnityEngine.Input.GetKey(KeyCode.K)) b |= 2;         // Skill (hero-spesifik)
            if (UnityEngine.Input.GetKey(KeyCode.Space)) b |= 4;     // Jump
            if (UnityEngine.Input.GetKey(KeyCode.LeftShift)) b |= 8; // Dash
            if (UnityEngine.Input.GetKey(KeyCode.L)) b |= 16;        // Guard
            if (UnityEngine.Input.GetKey(KeyCode.H)) b |= 32;        // Ganti hero (TITAN <-> PULSE)
            return b;
        }
    }
}
