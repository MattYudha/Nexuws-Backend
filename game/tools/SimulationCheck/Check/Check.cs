using System;
using System.Reflection;
using Photon.Deterministic;
using Quantum;

public static class Check
{
    static readonly MethodInfo Bounce = typeof(PongSystem).GetMethod("BounceOffPaddle", BindingFlags.NonPublic | BindingFlags.Static);

    static (FPVector2 pos, FPVector2 vel) Run(FPVector2 pos, FPVector2 vel, int paddleX, FP paddleY, int dir)
    {
        var args = new object[] { pos, vel, (FP)paddleX, paddleY, dir };
        Bounce.Invoke(null, args);
        return ((FPVector2)args[0], (FPVector2)args[1]);
    }

    static string LutDirectory()
    {
        foreach (var m in typeof(Check).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            if (m.Key == "LutDir") return m.Value;
        throw new InvalidOperationException("LutDir metadata missing");
    }

    static int failures;
    static void Expect(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }

    public static int Main()
    {
        FPLut.Init(LutDirectory());
        // left paddle at x=-7 (out direction +1): ball moving left hits its face at -6.75
        var (p, v) = Run(new FPVector2((FP)(-66) / 10, (FP)2 / 10), new FPVector2(-7, 0), -7, 0, 1);
        Expect(v.X > 0, "left paddle reflects ball to the right");
        Expect(p.X == (FP)(-27) / 4 + (FP)1 / 4, "ball is pushed out in front of the left paddle");
        Expect(v.Magnitude > 7, "ball speeds up on a hit");

        (p, v) = Run(new FPVector2((FP)66 / 10, (FP)(-5) / 10), new FPVector2(7, 0), 7, 0, -1);
        Expect(v.X < 0, "right paddle reflects ball to the left");
        Expect(v.Y < 0, "hit below paddle centre sends the ball downwards");

        (p, v) = Run(new FPVector2((FP)(-66) / 10, 3), new FPVector2(-7, 0), -7, 0, 1);
        Expect(v.X < 0, "ball far above the paddle is not reflected");

        (p, v) = Run(new FPVector2((FP)(-76) / 10, 0), new FPVector2(-7, 0), -7, 0, 1);
        Expect(v.X < 0, "ball already behind the paddle is not reflected");

        (p, v) = Run(new FPVector2(0, 0), new FPVector2(-7, 0), -7, 0, 1);
        Expect(v.X < 0, "ball in mid field is untouched");

        // determinism: same inputs, same raw result
        var a = Run(new FPVector2((FP)(-66) / 10, (FP)3 / 10), new FPVector2(-7, 1), -7, (FP)1 / 10, 1);
        var b = Run(new FPVector2((FP)(-66) / 10, (FP)3 / 10), new FPVector2(-7, 1), -7, (FP)1 / 10, 1);
        Expect(a.vel.X.RawValue == b.vel.X.RawValue && a.vel.Y.RawValue == b.vel.Y.RawValue, "bounce is deterministic");
        return failures;
    }
}
