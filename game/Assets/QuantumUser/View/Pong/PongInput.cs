using Photon.Deterministic;
using Quantum;
using UnityEngine;
using QuantumInput = Quantum.Input;

namespace Pong
{
    /// <summary>Turns keyboard / touch / mouse into the one-axis Pong input every tick.</summary>
    public unsafe class PongInput : MonoBehaviour
    {
        private const float DeadZone = 0.3f;

        private void OnEnable()
        {
            QuantumCallback.Subscribe(this, (CallbackPollInput callback) => Poll(callback));
        }

        private void Poll(CallbackPollInput callback)
        {
            var input = new QuantumInput { Move = ReadMove() };
            callback.SetInput(input, DeterministicInputFlags.Repeatable);
        }

        private static int ReadMove()
        {
            float axis = UnityEngine.Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(axis) > 0.1f) return axis > 0 ? 1 : -1;

            // Touch / mouse: the paddle follows the pointer's height.
            Vector3? pointer = null;
            if (UnityEngine.Input.touchCount > 0) pointer = UnityEngine.Input.GetTouch(0).position;
            else if (UnityEngine.Input.GetMouseButton(0)) pointer = UnityEngine.Input.mousePosition;
            if (pointer == null || Camera.main == null) return 0;

            var frame = PongGame.PredictedFrame();
            int slot = PongGame.LocalSlot;
            if (frame == null || slot < 0) return 0;

            float target = Camera.main.ScreenToWorldPoint(pointer.Value).y;
            float paddle = (slot == 0 ? frame.Global->LeftY : frame.Global->RightY).AsFloat;
            float delta = target - paddle;
            return Mathf.Abs(delta) < DeadZone ? 0 : (delta > 0 ? 1 : -1);
        }
    }
}
