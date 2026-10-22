using Photon.Deterministic;

namespace Quantum
{
    /// <summary>
    /// Deterministic Pong rules. Field is 16 x 9 units centered on the origin; slot 0 is the left
    /// paddle and slot 1 the right paddle. Everything here must stay free of floats and randomness.
    /// </summary>
    public unsafe class PongSystem : SystemMainThread, ISignalOnPlayerAdded
    {
        public const int StateWaiting = 0;
        public const int StatePlaying = 1;
        public const int StateOver = 2;

        /// <summary>Must equal "Game:WinScore" in the backend configuration.</summary>
        public const int WinScore = 5;

        // The default Quantum simulation rate is 60 ticks per second.
        private const int CountdownTicksPerRound = 60;
        private const int CountdownTicksBeforeFirstServe = 180;

        private static readonly FP HalfWidth = 8;
        private static readonly FP HalfHeight = (FP)9 / 2;
        private static readonly FP PaddleX = 7;
        private static readonly FP PaddleHalfWidth = (FP)1 / 4;
        private static readonly FP PaddleHalfHeight = 1;
        private static readonly FP PaddleSpeed = 9;
        private static readonly FP BallRadius = (FP)1 / 4;
        private static readonly FP InitialBallSpeed = 7;
        private static readonly FP BallSpeedUp = (FP)1 / 2;
        private static readonly FP MaxBallSpeed = 15;

        public override void Update(Frame f)
        {
            var g = f.Global;
            if (g->State != StatePlaying) return;

            MovePaddles(f);

            if (g->CountdownTicks > 0)
            {
                g->CountdownTicks--;
                if (g->CountdownTicks == 0) Serve(g);
                return;
            }

            MoveBall(f);
        }

        public void OnPlayerAdded(Frame f, PlayerRef player, bool firstTime)
        {
            var g = f.Global;
            int slot = (int)player;
            if (slot < 0 || slot > 1) return;

            g->JoinedMask |= 1 << slot;
            if (g->JoinedMask == 3 && g->State == StateWaiting)
            {
                g->State = StatePlaying;
                g->ScoreLeft = 0;
                g->ScoreRight = 0;
                g->ServeDirection = 1;
                ResetBall(g, CountdownTicksBeforeFirstServe);
            }
        }

        private static void MovePaddles(Frame f)
        {
            var g = f.Global;
            var limit = HalfHeight - PaddleHalfHeight;

            for (int slot = 0; slot < 2; slot++)
            {
                var input = f.GetPlayerInput(slot);
                int move = input == null ? 0 : (input->Move > 0 ? 1 : (input->Move < 0 ? -1 : 0));

                var y = slot == 0 ? g->LeftY : g->RightY;
                y = FPMath.Clamp(y + move * PaddleSpeed * f.DeltaTime, -limit, limit);

                if (slot == 0) g->LeftY = y;
                else g->RightY = y;
            }
        }

        private static void MoveBall(Frame f)
        {
            var g = f.Global;
            var pos = g->BallPosition + g->BallVelocity * f.DeltaTime;
            var vel = g->BallVelocity;

            var wall = HalfHeight - BallRadius;
            if (pos.Y > wall) { pos.Y = wall; vel.Y = -vel.Y; }
            else if (pos.Y < -wall) { pos.Y = -wall; vel.Y = -vel.Y; }

            if (vel.X < 0) BounceOffPaddle(ref pos, ref vel, -PaddleX, g->LeftY, 1);
            else BounceOffPaddle(ref pos, ref vel, PaddleX, g->RightY, -1);

            g->BallPosition = pos;
            g->BallVelocity = vel;

            if (pos.X < -HalfWidth) Score(g, rightScored: true);
            else if (pos.X > HalfWidth) Score(g, rightScored: false);
        }

        private static void BounceOffPaddle(ref FPVector2 pos, ref FPVector2 vel, FP paddleX, FP paddleY, int outDirection)
        {
            // Inner face of the paddle (the side the ball hits) and its back face.
            var face = paddleX + outDirection * PaddleHalfWidth;
            var back = paddleX - outDirection * PaddleHalfWidth;

            bool reachedFace = outDirection > 0 ? pos.X - BallRadius <= face : pos.X + BallRadius >= face;
            bool notPastBack = outDirection > 0 ? pos.X + BallRadius >= back : pos.X - BallRadius <= back;
            bool overlapsY = FPMath.Abs(pos.Y - paddleY) <= PaddleHalfHeight + BallRadius;
            if (!reachedFace || !notPastBack || !overlapsY) return;

            pos.X = face + outDirection * BallRadius;

            // Where the ball hit the paddle decides the outgoing angle (-1 bottom .. 1 top).
            var offset = FPMath.Clamp((pos.Y - paddleY) / (PaddleHalfHeight + BallRadius), -1, 1);
            var speed = FPMath.Min(vel.Magnitude + BallSpeedUp, MaxBallSpeed);
            var direction = new FPVector2(outDirection, offset * 3 / 4).Normalized;
            vel = direction * speed;
        }

        private static void Score(_globals_* g, bool rightScored)
        {
            if (rightScored)
            {
                g->ScoreRight++;
                g->ServeDirection = -1;
            }
            else
            {
                g->ScoreLeft++;
                g->ServeDirection = 1;
            }

            if (g->ScoreLeft >= WinScore || g->ScoreRight >= WinScore)
            {
                g->State = StateOver;
                g->BallVelocity = default;
                return;
            }

            ResetBall(g, CountdownTicksPerRound);
        }

        private static void ResetBall(_globals_* g, int countdownTicks)
        {
            g->BallPosition = default;
            g->BallVelocity = default;
            g->CountdownTicks = countdownTicks;
        }

        private static void Serve(_globals_* g)
        {
            // Deterministic pseudo-variation of the serve angle: -2, 0 or 2 units per second.
            int tilt = ((g->ScoreLeft + g->ScoreRight) % 3 - 1) * 2;
            g->BallVelocity = new FPVector2(g->ServeDirection * InitialBallSpeed, tilt);
        }
    }
}
