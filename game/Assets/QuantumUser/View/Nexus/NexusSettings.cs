using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Nexus
{
    /// <summary>
    /// Settings lokal (docs/31 §1.6): simpan di PlayerPrefs, LANGSUNG diterapkan. Satu sumber untuk lobby (UI) &amp; HUD.
    /// Hanya opsi yang benar-benar berfungsi; yang belum ada sistemnya (musik/SFX terpisah, rebinding, ping) = "Segera Hadir" di UI.
    /// </summary>
    public static class NexusSettings
    {
        private const string KRes = "nx.gfx.res", KMode = "nx.gfx.mode", KVsync = "nx.gfx.vsync", KFps = "nx.gfx.fps",
                             KQuality = "nx.gfx.quality", KMaster = "nx.audio.master", KMute = "nx.audio.mute", KShowFps = "nx.ui.showfps";

        public static readonly int[] FpsCaps = { 30, 60, 120, 144, 0 };             // 0 = tanpa batas
        public static readonly string[] ScreenModes = { "Layar penuh", "Borderless", "Jendela" };
        private static readonly FullScreenMode[] ModeValues = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };

        /// <summary>Resolusi unik (lebar×tinggi), terkecil → terbesar.</summary>
        public static List<string> Resolutions() =>
            Screen.resolutions.Select(r => $"{r.width}x{r.height}").Distinct().ToList();

        public static string Resolution
        {
            get => PlayerPrefs.GetString(KRes, $"{Screen.width}x{Screen.height}");
            set { PlayerPrefs.SetString(KRes, value); ApplyDisplay(); }
        }
        public static int ScreenMode
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(KMode, Screen.fullScreenMode == FullScreenMode.Windowed ? 2 : Screen.fullScreenMode == FullScreenMode.FullScreenWindow ? 1 : 0), 0, 2);
            set { PlayerPrefs.SetInt(KMode, value); ApplyDisplay(); }
        }
        public static bool VSync
        {
            get => PlayerPrefs.GetInt(KVsync, 1) == 1;
            set { PlayerPrefs.SetInt(KVsync, value ? 1 : 0); ApplyFrame(); }
        }
        public static int FpsCap
        {
            get => PlayerPrefs.GetInt(KFps, 60);
            set { PlayerPrefs.SetInt(KFps, value); ApplyFrame(); }
        }
        public static int Quality
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(KQuality, QualitySettings.GetQualityLevel()), 0, QualitySettings.names.Length - 1);
            set { PlayerPrefs.SetInt(KQuality, value); QualitySettings.SetQualityLevel(value, true); }
        }
        public static int MasterVolume
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(KMaster, 80), 0, 100);
            set { PlayerPrefs.SetInt(KMaster, Mathf.Clamp(value, 0, 100)); ApplyAudio(); }
        }
        public static bool Mute
        {
            get => PlayerPrefs.GetInt(KMute, 0) == 1;
            set { PlayerPrefs.SetInt(KMute, value ? 1 : 0); ApplyAudio(); }
        }
        public static bool ShowFps
        {
            get => PlayerPrefs.GetInt(KShowFps, 0) == 1;
            set => PlayerPrefs.SetInt(KShowFps, value ? 1 : 0);
        }

        /// <summary>Terapkan semua (dipanggil sekali saat lobby siap).</summary>
        public static void ApplyAll()
        {
            ApplyDisplay(); ApplyFrame(); ApplyAudio();
            if (PlayerPrefs.HasKey(KQuality)) QualitySettings.SetQualityLevel(Quality, true);
        }

        private static void ApplyDisplay()
        {
            if (Application.isEditor) return; // editor: jangan ubah ukuran Game view
            var parts = Resolution.Split('x');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int w) || !int.TryParse(parts[1], out int h)) return;
            Screen.SetResolution(w, h, ModeValues[ScreenMode]);
        }

        // VSync aktif → Unity mengabaikan targetFrameRate; batas FPS berlaku saat VSync mati.
        private static void ApplyFrame()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FpsCap > 0 ? FpsCap : -1;
        }

        private static void ApplyAudio() => AudioListener.volume = Mute ? 0f : MasterVolume / 100f;
    }
}
