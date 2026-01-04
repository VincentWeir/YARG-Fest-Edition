using DG.Tweening;
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Logging;
using YARG.Gameplay.Player;
using YARG.Helpers.Extensions;
using YARG.Menu.MusicLibrary;
using YARG.Player;
using YARG.Settings;

namespace YARG.Gameplay.HUD
{
    public class PlayerNameDisplay : GameplayBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _playerName;
        [SerializeField]
        private Image _instrumentIcon;
        [SerializeField]
        private GameObject _padModeIcon;
        [SerializeField]
        private RawImage _needleIcon;

        private CanvasGroup _canvasGroup;

        public float DisplayTime = 3.0f;
        public float FadeDuration = 0.5f;

        protected override void GameplayAwake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
        }

        public void ShowPlayer(YargPlayer player)
        {
            if (!ShouldShowPlayer())
            {
                return;
            }

            var profile = player.Profile;
            _playerName.text = profile.Name;

            var spriteName = GetSpriteName(profile.CurrentInstrument, profile.HarmonyIndex);
            _instrumentIcon.sprite = Addressables
                .LoadAssetAsync<Sprite>(spriteName)
                .WaitForCompletion();

            if (!MusicLibraryMenu.isProMode)
            {
                _padModeIcon.SetActive(true);
            }
            else
            {
                _padModeIcon.SetActive(false);
            }

            StartCoroutine(FadeoutCoroutine());
        }

        public void ShowPlayer(YargPlayer player, int needleId)
        {
            if (!ShouldShowPlayer())
            {
                return;
            }

            var textureNeedle = $"VocalNeedleTexture/{needleId}";
            _needleIcon.texture = Addressables.LoadAssetAsync<Texture2D>(textureNeedle).WaitForCompletion();
            _instrumentIcon.color = GetHarmonyColor(player);
            ShowPlayer(player);
        }

        private bool ShouldShowPlayer()
        {
            return !GameManager.IsPractice && SettingsManager.Settings.ShowPlayerNameWhenStartingSong.Value;
        }

        private string GetSpriteName(Instrument currentInstrument, byte harmonyIndex)
        {
            if (currentInstrument == Instrument.Harmony)
            {
                return $"HarmonyVocalsIcons[{harmonyIndex + 1}]";
            }

            // Base resource name for the instrument (e.g. "guitar", "drums", etc.)
            var baseName = currentInstrument.ToResourceName() ?? string.Empty;

            // Detect drums robustly: either enum name contains "Drum" or resource name contains "drum".
            bool IsDrums()
            {
                try
                {
                    var enumName = currentInstrument.ToString() ?? string.Empty;
                    if (enumName.IndexOf("Drum", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                catch
                {
                    // ignore and fall back to resource-name check
                }

                return baseName.IndexOf("drum", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            // If instrument is a drum (standard/pro), always use realDrums sprite key.
            if (IsDrums())
            {
                return $"InstrumentIcons[realDrums]";
            }

            // If pro mode is enabled, prefix "real" and capitalize the instrument name (e.g. "realGuitar")
            if (MusicLibraryMenu.isProMode && !string.IsNullOrEmpty(baseName))
            {
                baseName = "real" + Capitalize(baseName);
            }

            return $"InstrumentIcons[{baseName}]";
        }

        private string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private Color GetHarmonyColor(YargPlayer player)
        {
            if (player.Profile.CurrentInstrument != Instrument.Harmony)
            {
                return Color.white;
            }

            if (player.Profile.HarmonyIndex >= VocalTrack.Colors.Length)
            {
                YargLogger.LogWarning("PlayerNameDisplay", $"Harmony index {player.Profile.HarmonyIndex} is out of bounds.");
                return Color.white;
            }

            return VocalTrack.Colors[player.Profile.HarmonyIndex];
        }

        private IEnumerator FadeoutCoroutine()
        {
            _canvasGroup.alpha = 1f;
            yield return new WaitForSeconds(DisplayTime);
            yield return _canvasGroup.DOFade(0f, FadeDuration).WaitForCompletion();

            gameObject.SetActive(false);
        }
    }
}