using System;
using UnityEngine;

namespace Companion
{
    public enum DayPeriod { Night, Dawn, Day, Dusk }

    // Picks the background sky from the device's local time when the app opens (and again when it
    // returns from the background in a different period): a random skybox from that period's set.
    // The skyboxes live in Resources/Sky (built by Companion/Build Skyboxes) so only the chosen one
    // is loaded. The painted backdrop quad stays as the fallback if a skybox is missing.
    public class TimeOfDaySky : MonoBehaviour
    {
        // Resources/Sky/<name>; the editor builder copies each one from its Allsky source material.
        // rotation (degrees) turns the best-looking side of the sky behind the character.
        public static readonly (DayPeriod period, string name, string source, float rotation)[] Skies =
        {
            (DayPeriod.Night, "Night_CartoonSkywave", "Cartoon/Cartoon Base Skywave/Cartoon Base Skywave.mat", 0),
            (DayPeriod.Night, "Night_CartoonTwilight", "Cartoon/Cartoon Base Twilight/Cartoon Base Twilight.mat", 180),
            (DayPeriod.Dawn, "Dawn_FantasyHigh", "Fantasy/Fantasy High/FantasyClouds2_High.mat", 90),
            (DayPeriod.Dawn, "Dawn_FantasyMid2", "Fantasy/Fantasy Mid 2/AllSky_FantasySky_Mid_2.mat", 180),
            (DayPeriod.Dawn, "Dawn_Haze", "Haze/Haze_DiffuseLowSunGreen/Haze_DiffuseLowSunGreen.mat", 180),
            (DayPeriod.Dawn, "Dawn_UmlangaSunrise", "HDRIHaven/Umlanga Sunrise/Umlanga Sunrise.mat", 0),
            (DayPeriod.Day, "Day_FantasyHigh3", "Fantasy/Fantasy High 3/FantasyClouds_High3.mat", 0),
            (DayPeriod.Day, "Day_CayleyLookout", "HDRIHaven/Cayley Lookout/Cayley Lookout.mat", 0),
            (DayPeriod.Day, "Day_CloudLayers", "HDRIHaven/Cloud Layers/Cloud Layers.mat", 270),
            (DayPeriod.Day, "Day_RockyRidge", "HDRIHaven/Rocky Ridge/Rocky Ridge.mat", 0),
            (DayPeriod.Day, "Day_TableMountain", "HDRIHaven/Table Mountain/Table Mountain.mat", 0),
            (DayPeriod.Day, "Day_ColdClouds", "Version 4/Cold Clouds/Cold Clouds.mat", 0),
            (DayPeriod.Dusk, "Dusk_PinkSunset", "Version 4/Pink Sunset/Pink Sunset.mat", 180),
            (DayPeriod.Dusk, "Dusk_PinkSunset2", "Version 4/Pink Sunset 2/Pink Sunset 2.mat", 180),
        };

        [SerializeField] Camera targetCamera;
        [Tooltip("Shown instead when no skybox can be loaded.")]
        [SerializeField] GameObject backdrop;
        [Tooltip("Local hour each period starts at.")]
        [SerializeField, Range(0, 23)] int dawnStart = 5, dayStart = 8, duskStart = 17, nightStart = 19;
        [Tooltip("Editor testing: use this period instead of the clock.")]
        [SerializeField] bool overridePeriod;
        [SerializeField] DayPeriod period;

        DayPeriod? shown;

        void Start() => Apply();

        void OnApplicationPause(bool pause)
        {
            if (!pause && Current() != shown) Apply();
        }

        public DayPeriod Current()
        {
            if (overridePeriod) return period;
            int h = DateTime.Now.Hour;
            if (h >= nightStart || h < dawnStart) return DayPeriod.Night;
            if (h < dayStart) return DayPeriod.Dawn;
            if (h < duskStart) return DayPeriod.Day;
            return DayPeriod.Dusk;
        }

        void Apply()
        {
            var p = Current();
            var names = Array.FindAll(Skies, s => s.period == p);
            var sky = Resources.Load<Material>("Sky/" + names[UnityEngine.Random.Range(0, names.Length)].name);
            shown = p;
            if (sky == null)
            {
                Debug.LogWarning("TimeOfDaySky: skybox missing, run Companion/Build Skyboxes.");
                return;
            }
            RenderSettings.skybox = sky;
            if (targetCamera != null) targetCamera.clearFlags = CameraClearFlags.Skybox;
            if (backdrop != null) backdrop.SetActive(false);
        }
    }
}
