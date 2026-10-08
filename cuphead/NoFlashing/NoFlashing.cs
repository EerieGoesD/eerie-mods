using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NoFlashing
{
    public enum FilmGrainMode
    {
        Off,
        Still,
        Flickering
    }

    [BepInPlugin("eeriegoesd.cuphead.noflashing", "No Flashing", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> HitFlashes;
        internal static ConfigEntry<bool> PlayerFlashes;
        internal static ConfigEntry<bool> SceneryFlashes;
        internal static ConfigEntry<bool> LevelIntro;
        internal static ConfigEntry<bool> BossExplosions;
        internal static ConfigEntry<FilmGrainMode> FilmGrain;
        internal static ConfigEntry<bool> ScreenShake;

        // Which setting each patched game function belongs to. Looked up by the shared patch methods.
        internal static readonly Dictionary<MethodBase, ConfigEntry<bool>> Settings = new Dictionary<MethodBase, ConfigEntry<bool>>();

        private Harmony harmony;
        private int applied;
        private int failed;

        private void Awake()
        {
            HitFlashes = Config.Bind("Settings", "Remove hit flashes", true,
                "Bosses and enemies no longer flash when hit or when they die.");
            PlayerFlashes = Config.Bind("Settings", "Remove player flashes", true,
                "The player no longer blinks after taking damage (they stay see-through until they can be hit again) and no longer flashes when the healing charm triggers.");
            SceneryFlashes = Config.Bind("Settings", "Remove scenery flashes", true,
                "No background lightning, flickering lights, Saltbaker sky flash, slot machine flash, blinking prompts or pollen screen wobble.");
            LevelIntro = Config.Bind("Settings", "Steady level intro", true,
                "The Ready / WALLOP! text no longer strobes.");
            BossExplosions = Config.Bind("Settings", "Remove boss explosions", true,
                "No explosion bursts when a boss is knocked out. The sound still plays.");
            FilmGrain = Config.Bind("Settings", "Film grain", FilmGrainMode.Off,
                "Off removes the old-film grain. Still keeps it but stops it flickering. Flickering is the game's normal look.");
            ScreenShake = Config.Bind("Settings", "Remove screen shake", true,
                "Removes all screen shake.");

            harmony = new Harmony("eeriegoesd.cuphead.noflashing");

            Prefix(typeof(HitFlash), "Flash", "Skip", HitFlashes);
            Prefix(typeof(AirshipJellyLevelJelly), "flash_cr", "SkipCoroutine", HitFlashes);
            Prefix(typeof(AbstractLevelEntity), "dieFlash_cr", "DieFlashAsWait", HitFlashes);

            Prefix(typeof(LevelPlayerAnimationController), "flash_cr", "SteadyPlayerDim", PlayerFlashes);
            Prefix(typeof(PlanePlayerAnimationController), "flash_cr", "SteadyPlayerDim", PlayerFlashes);
            Prefix(typeof(ArcadePlayerAnimationController), "flash_cr", "SteadyPlayerDim", PlayerFlashes);
            Prefix(typeof(HealerCharmSparkEffect), "player_flash_cr", "SkipHealerFlash", PlayerFlashes);

            Prefix(typeof(DragonLevel), "lightning_cr", "SkipCoroutine", SceneryFlashes);
            Prefix(typeof(ChessKnightLevelLightningController), "lightning_cr", "SkipCoroutine", SceneryFlashes);
            Prefix(typeof(SaltbakerLevel), "flash_sky_cr", "SteadySky", SceneryFlashes);
            Prefix(typeof(ClownLevelBackgroundLights), "flicker_cr", "SkipCoroutine", SceneryFlashes);
            Prefix(typeof(SallyStagePlayLevelBackgroundHandler), "flicker_cr", "SkipCoroutine", SceneryFlashes);
            Prefix(typeof(LevelLightFlicker), "flicker_cr", "SkipCoroutine", SceneryFlashes);
            Prefix(typeof(DicePalaceMainLevelGameManager), "flashEnd_cr", "SkipCoroutine", SceneryFlashes);
            Prefix(typeof(FrogsLevelMorphedSlot), "flash_cr", "SkipCoroutine", SceneryFlashes);
            Prefix(typeof(CupheadRenderer), "TouchFuzzy", "Skip", SceneryFlashes);
            Prefix(typeof(FlashingPrompt), "Update", "SteadyPrompt", SceneryFlashes);

            Postfix(typeof(LevelIntroAnimation), "Awake", "AddSteadyIntro", LevelIntro);
            Postfix(typeof(PlatformingLevelIntroAnimation), "Awake", "AddSteadyIntro", LevelIntro);

            Prefix(typeof(LevelBossDeathExploder), "go_cr", "ExplosionSoundOnly", BossExplosions);

            Prefix(typeof(ChromaticAberrationFilmGrain), "OnRenderImage", "ApplyGrainMode", null);
            Prefix(typeof(ScreenOverlayAnimated), "OnRenderImage", "ApplyOverlayMode", null);

            Postfix(typeof(AbstractCupheadGameCamera), "Shake", "CancelShake", ScreenShake);
            Postfix(typeof(AbstractCupheadGameCamera), "StartShake", "CancelShake", ScreenShake);
            Postfix(typeof(AbstractCupheadGameCamera), "EndShake", "CancelShake", ScreenShake);
            Postfix(typeof(AbstractCupheadGameCamera), "StartSmoothShake", "CancelShake", ScreenShake);

            Logger.LogInfo("Patched " + applied + " effects, " + failed + " failed.");
        }

        private void Prefix(Type type, string method, string patch, ConfigEntry<bool> setting)
        {
            Apply(type, method, patch, setting, true);
        }

        private void Postfix(Type type, string method, string patch, ConfigEntry<bool> setting)
        {
            Apply(type, method, patch, setting, false);
        }

        private void Apply(Type type, string method, string patch, ConfigEntry<bool> setting, bool prefix)
        {
            try
            {
                MethodInfo original = AccessTools.Method(type, method);
                if (original == null)
                {
                    Logger.LogWarning("Not found: " + type.Name + "." + method);
                    failed++;
                    return;
                }
                if (setting != null)
                {
                    Settings[original] = setting;
                }
                HarmonyMethod hook = new HarmonyMethod(AccessTools.Method(typeof(Patches), patch));
                if (prefix)
                {
                    harmony.Patch(original, hook, null);
                }
                else
                {
                    harmony.Patch(original, null, hook);
                }
                applied++;
            }
            catch (Exception e)
            {
                Logger.LogError("Could not patch " + type.Name + "." + method + ": " + e.Message);
                failed++;
            }
        }
    }

    internal static class Patches
    {
        private const float PlayerDimAlpha = 0.5f;
        private const float DieFlashSeconds = 2f;

        private static readonly Dictionary<Type, MethodInfo[]> playerMethods = new Dictionary<Type, MethodInfo[]>();
        private static readonly Dictionary<Behaviour, float> grainIntensity = new Dictionary<Behaviour, float>();

        private static bool On(MethodBase original)
        {
            ConfigEntry<bool> setting;
            return Plugin.Settings.TryGetValue(original, out setting) && setting.Value;
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }

        private static bool Skip(MethodBase __originalMethod)
        {
            return !On(__originalMethod);
        }

        private static bool SkipCoroutine(MethodBase __originalMethod, ref IEnumerator __result)
        {
            if (!On(__originalMethod))
            {
                return true;
            }
            __result = Nothing();
            return false;
        }

        // The death flash runs red to black four times over two seconds. Keep the two seconds so the death plays out the same.
        private static bool DieFlashAsWait(MethodBase __originalMethod, MonoBehaviour __instance, ref IEnumerator __result)
        {
            if (!On(__originalMethod))
            {
                return true;
            }
            __result = Wait(__instance, DieFlashSeconds);
            return false;
        }

        private static IEnumerator Wait(MonoBehaviour owner, float seconds)
        {
            yield return CupheadTime.WaitForSeconds(owner, seconds);
        }

        private static bool SteadyPlayerDim(MethodBase __originalMethod, Component __instance, ref IEnumerator __result)
        {
            if (!On(__originalMethod))
            {
                return true;
            }
            Type type = __instance.GetType();
            MethodInfo[] methods;
            if (!playerMethods.TryGetValue(type, out methods))
            {
                methods = new MethodInfo[] { AccessTools.PropertyGetter(type, "Flashing"), AccessTools.Method(type, "SetAlpha") };
                playerMethods[type] = methods;
            }
            if (methods[0] == null || methods[1] == null)
            {
                return true;
            }
            __result = SteadyDim(__instance, methods[0], methods[1]);
            return false;
        }

        private static IEnumerator SteadyDim(Component controller, MethodInfo flashing, MethodInfo setAlpha)
        {
            bool dimmed = false;
            while (controller != null)
            {
                bool now = (bool)flashing.Invoke(controller, null);
                if (now != dimmed)
                {
                    dimmed = now;
                    setAlpha.Invoke(controller, new object[] { now ? PlayerDimAlpha : 1f });
                }
                yield return null;
            }
        }

        private static bool SkipHealerFlash(MethodBase __originalMethod, Component __instance, ref IEnumerator __result)
        {
            if (!On(__originalMethod))
            {
                return true;
            }
            __result = DestroyNow(__instance.gameObject);
            return false;
        }

        private static IEnumerator DestroyNow(GameObject effect)
        {
            UnityEngine.Object.Destroy(effect);
            yield break;
        }

        // The sky jumps to white and fades back to black over and over. Hold it at black, where it rests between flashes.
        private static bool SteadySky(MethodBase __originalMethod, SpriteRenderer ___skyFront, ref IEnumerator __result)
        {
            if (!On(__originalMethod))
            {
                return true;
            }
            if (___skyFront != null)
            {
                ___skyFront.color = Color.black;
            }
            __result = Nothing();
            return false;
        }

        // The prompt shows while the timer is under 0.75 and hides above it. Holding the timer at 0 keeps it shown.
        private static void SteadyPrompt(MethodBase __originalMethod, ref float ___flashTimer)
        {
            if (On(__originalMethod))
            {
                ___flashTimer = 0f;
            }
        }

        private static void AddSteadyIntro(MonoBehaviour __instance)
        {
            __instance.gameObject.AddComponent<SteadyIntro>();
        }

        private static bool ExplosionSoundOnly(MethodBase __originalMethod, bool ___disableSound, ref IEnumerator __result)
        {
            if (!On(__originalMethod))
            {
                return true;
            }
            __result = PlayExplosionSound(___disableSound);
            return false;
        }

        private static IEnumerator PlayExplosionSound(bool disableSound)
        {
            if (!disableSound)
            {
                AudioManager.Play("level_explosion_boss_death");
            }
            yield break;
        }

        private static void ApplyGrainMode(ChromaticAberrationFilmGrain __instance)
        {
            __instance.animated = Plugin.FilmGrain.Value == FilmGrainMode.Flickering;
            __instance.intensity = GrainIntensity(__instance, __instance.intensity);
        }

        private static void ApplyOverlayMode(ScreenOverlayAnimated __instance)
        {
            __instance.animated = Plugin.FilmGrain.Value == FilmGrainMode.Flickering;
            __instance.intensity = GrainIntensity(__instance, __instance.intensity);
        }

        // Remembers the game's own intensity the first time it is seen, so Off can be undone from the settings window.
        private static float GrainIntensity(Behaviour effect, float current)
        {
            float original;
            if (!grainIntensity.TryGetValue(effect, out original))
            {
                original = current;
                grainIntensity[effect] = original;
            }
            return Plugin.FilmGrain.Value == FilmGrainMode.Off ? 0f : original;
        }

        private static void CancelShake(MethodBase __originalMethod, AbstractCupheadGameCamera __instance)
        {
            if (On(__originalMethod))
            {
                __instance.ResetShake();
            }
        }
    }

    // The intro text animation alternates white and black frames to strobe. Whenever a black frame comes up, the last white frame stays on screen instead.
    internal class SteadyIntro : MonoBehaviour
    {
        private static readonly HashSet<string> darkFrames = new HashSet<string>
        {
            "FightText_GetReady_0029", "FightText_GetReady_0031", "FightText_GetReady_0033", "FightText_GetReady_0035", "FightText_GetReady_0037",
            "FightText_GetReady_0039", "FightText_GetReady_0041", "FightText_GetReady_0043", "FightText_GetReady_0045", "FightText_GetReady_0047",
            "FightText_EnGarde_0029",
            "level_text_rungun_0007", "level_text_rungun_0009"
        };

        private Image[] images;
        private SpriteRenderer[] renderers;
        private Sprite lastLight;

        private void Awake()
        {
            images = GetComponentsInChildren<Image>(true);
            renderers = GetComponentsInChildren<SpriteRenderer>(true);
        }

        private void LateUpdate()
        {
            if (!Plugin.LevelIntro.Value)
            {
                return;
            }
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null || image.sprite == null)
                {
                    continue;
                }
                if (darkFrames.Contains(image.sprite.name))
                {
                    if (lastLight != null)
                    {
                        image.sprite = lastLight;
                    }
                }
                else
                {
                    lastLight = image.sprite;
                }
            }
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null || renderer.sprite == null)
                {
                    continue;
                }
                if (darkFrames.Contains(renderer.sprite.name))
                {
                    if (lastLight != null)
                    {
                        renderer.sprite = lastLight;
                    }
                }
                else
                {
                    lastLight = renderer.sprite;
                }
            }
        }
    }
}
