using System;
using System.Collections.Generic;
using UnityEngine;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Gameplay.Player;
using YARG.Helpers.Extensions;
using YARG.Menu.MusicLibrary;
using YARG.Themes;
using Color = System.Drawing.Color;

namespace YARG.Gameplay.Visuals
{
    public class FretArray : MonoBehaviour
    {
        private const float WIDTH_NUMERATOR   = 2f;
        private const float WIDTH_DENOMINATOR = 5f;

        // PUBLIC API (preserved)
        public int FretCount;
        public bool DontFlipColorsLeftyFlip;
        public bool UseKickFrets;

        [SerializeField]
        private float _trackWidth = 2f;

        [Space]
        [SerializeField]
        private Transform _leftKickFretPosition;
        [SerializeField]
        private Transform _rightKickFretPosition;

        [Tooltip("Assign the Pad Mode Lane Separator transform (optional). If set, its localPosition.x will be adjusted when 4-lane visuals are applied.")]
        [SerializeField]
        private Transform PadModeLaneSeparator;

        [Tooltip("How much of the inter-fret spacing a fret should occupy (0-1). 1.0 fills fully; 0.9 leaves breathing room.")]
        [SerializeField]
        private float FretFillFactor = 0.9f;

        [Tooltip("Multiplier applied to gem/note widths and scales relative to inter-fret spacing. 1.0 = full spacing.")]
        [SerializeField]
        private float NoteFillFactor = 1.0f;

        private readonly List<Fret> _frets = new();
        private readonly List<KickFret> _kickFrets = new();

        private bool[] _activeFrets;
        private bool[] _pulsingFrets;
        private float  _pulseDuration;

        // Visual-mapping fields (new)
        private bool _usingVisualFretCount = false;
        private float[] _logicalToVisualX;
        private float _visualSpacing = 0f;

        // Store each fret's base X-scale so we can compute new scales without compounding
        private readonly List<float> _initialFretScaleXs = new();

        public void Initialize(ThemePreset themePreset, GameMode gameMode,
            ColorProfile.IFretColorProvider fretColorProvider, bool leftyFlip, bool splitProTomsAndCymbals, bool swapSnareAndHiHat, bool swapCrashAndRide)
        {
            var fretPrefab = ThemeManager.Instance.CreateFretPrefabFromTheme(
                themePreset, gameMode);

            // Spawn in normal frets
            _frets.Clear();
            _initialFretScaleXs.Clear();
            for (int i = 0; i < FretCount; i++)
            {
                int effectivePosition = i switch
                {
                    0 => swapSnareAndHiHat ? 1 : 0,
                    1 => swapSnareAndHiHat ? 0 : 1,
                    3 => swapCrashAndRide ? 5 : 3,
                    5 => swapCrashAndRide ? 3 : 5,
                    _ => i
                };

                // Spawn
                var fret = Instantiate(fretPrefab, transform);
                fret.SetActive(true);

                // Position (original prefab positions are based on FretCount)
                float x = _trackWidth / FretCount * effectivePosition - _trackWidth / 2f + 1f / FretCount;
                fret.transform.localPosition = new Vector3(leftyFlip ? -x : x, 0f, 0f);

                // Scale (original base scale calculation)
                float scale = (_trackWidth / WIDTH_NUMERATOR) / (FretCount / WIDTH_DENOMINATOR);
                fret.transform.localScale = new Vector3(scale, 1f, 1f);

                // Add
                var fretComp = fret.GetComponent<Fret>();
                _frets.Add(fretComp);

                // Record base X-scale (so ApplyFretLayout later can compute new scale relative to base)
                _initialFretScaleXs.Add(scale);
            }

            _kickFrets.Clear();
            if (UseKickFrets)
            {
                var kickFretPrefab = ThemeManager.Instance.CreateKickFretPrefabFromTheme(
                    themePreset, gameMode);

                // Spawn in kick frets
                var leftKick = Instantiate(kickFretPrefab, transform);
                leftKick.SetActive(true);
                var rightKick = Instantiate(kickFretPrefab, transform);
                rightKick.SetActive(true);

                // Position kick frets
                leftKick.transform.localPosition = _leftKickFretPosition.localPosition;
                rightKick.transform.localPosition = _rightKickFretPosition.localPosition;
                rightKick.transform.localScale = rightKick.transform.localScale.InvertX();

                // Add kick frets
                _kickFrets.Add(leftKick.GetComponent<KickFret>());
                _kickFrets.Add(rightKick.GetComponent<KickFret>());
            }

            // Initialize colors using existing logic
            InitializeColor(fretColorProvider, leftyFlip, splitProTomsAndCymbals);

            // --- NEW: apply visual-only 4-lane layout for non-Expert five-fret selections ---
            // This is purely visual: it repositions/hides the extra fret(s) while preserving
            // internal arrays and the original FretCount field so gameplay code remains unchanged.
            int? desired = DetectDesiredFretCountFromProfile();
            if (desired != null)
            {
                ApplyFretLayout(desired.Value, leftyFlip);
                // inside ApplyFretLayout we will call UpdatePadModeLaneSeparator(true)
            }
            else
            {
                // ensure separator returns to default
                UpdatePadModeLaneSeparator(false);
            }
            // --- END NEW ---

            _activeFrets = new bool[FretCount];
            _pulsingFrets = new bool[FretCount];
            // Start with all frets active, they will be set inactive once TrackPlayer figures itself out
            for (int i = 0; i < FretCount; i++)
            {
                _activeFrets[i] = true;
            }
        }

        public void InitializeColor(ColorProfile.IFretColorProvider fretColorProvider, bool leftyFlip, bool splitProTomsAndCymbals)
        {
            for (int i = 0; i < _frets.Count; i++)
            {
                // This needs unique lefty flip logic because it's the one case where
                // the fret order is different from the color profile order
                int index;
                if (splitProTomsAndCymbals)
                {
                    index = i switch
                    {
                        0 => leftyFlip ? 4 : 1,
                        1 => leftyFlip ? 7 : 6,
                        2 => leftyFlip ? 3 : 2,
                        3 => leftyFlip ? 6 : 7,
                        4 => leftyFlip ? 2 : 3,
                        5 => leftyFlip ? 5 : 8,
                        6 => leftyFlip ? 1 : 4,
                        _ => throw new Exception("Unreachable.")
                    };
                }
                else
                {
                    index = i + 1;
                }

                if (DontFlipColorsLeftyFlip && leftyFlip && !splitProTomsAndCymbals)
                {
                    index = _frets.Count - index + 1;
                }

                _frets[i].Initialize(
                    fretColorProvider.GetFretColor(index),
                    fretColorProvider.GetFretInnerColor(index),
                    fretColorProvider.GetParticleColor(index),
                    fretColorProvider.GetParticleColor(0 /* open note */)
                );
            }

            foreach (var kick in _kickFrets)
            {
                // Keep original kick initializer (one-arg in repo)
                kick.Initialize(fretColorProvider.GetFretColor(0));
            }
        }

        public void SetPressed(int index, bool pressed)
        {
            _frets[index].SetPressed(pressed);
        }

        public void SetSustained(int index, bool sustained)
        {
            _frets[index].SetSustained(sustained);
        }

        public void PlayHitAnimation(int index)
        {
            _frets[index].PlayHitAnimation();
            _frets[index].PlayHitParticles();
        }

        public void PlayOpenHitAnimation()
        {
            foreach (var fret in _frets)
            {
                fret.PlayHitAnimation();
                fret.PlayOpenHitParticles();
            }
        }

        public void PlayMissAnimation(int index)
        {
            _frets[index].PlayMissAnimation();
            _frets[index].PlayMissParticles();
        }

        public void PlayOpenMissAnimation()
        {
            foreach (var fret in _frets)
            {
                fret.PlayOpenMissAnimation();
                fret.PlayOpenMissParticles();
            }
        }

        public void PlayKickFretAnimation()
        {
            foreach (var kick in _kickFrets)
            {
                kick.PlayHitAnimation();
            }
        }

        public void ResetAll()
        {
            foreach (var fret in _frets)
            {
                fret.SetSustained(false);
            }
        }

        public void SetFretColorPulse(int fretIndex, bool pulse, float duration)
        {
            _pulseDuration = duration;
            _pulsingFrets[fretIndex] = pulse;
        }

        public void PulseFretColors()
        {
            for (int i = 0; i < _pulsingFrets.Length; i++)
            {
                if (!_pulsingFrets[i] || _activeFrets[i])
                {
                    continue;
                }

                _frets[i].FadeColor(_pulseDuration, true, false);
            }
        }

        public void UpdateFretActiveState(bool[] frets)
        {
            // We should always receive the same number of frets that we actually have, but...
            if (frets.Length != _frets.Count)
            {
                YargLogger.LogFormatDebug("Received inconsistent fret array. Got {0} flags, but we have {1} frets.", frets.Length, _frets.Count);
                return;
            }

            for (int i = 0; i < _frets.Count; i++)
            {
                if (_activeFrets[i] != frets[i])
                {
                    if (frets[i])
                    {
                        _frets[i].ResetColor(true);
                    }
                    else
                    {
                        _frets[i].DimColor(true);
                    }
                }

                _activeFrets[i] = frets[i];
            }
        }

        // ----------------- Helper methods for visual-only 4-lane layout -----------------

        // Decide whether we should show 4 visual lanes instead of prefab default.
        // Returns null to keep prefab default (do nothing).
        private int? DetectDesiredFretCountFromProfile()
        {
            try
            {
                // If the application/scene is in Pro mode, do not apply any 4-lane visuals.
                // Replace the following check with your own global flag if you have one:
                if (YARG.Menu.MusicLibrary.MusicLibraryMenu.isProMode)
                {
                    return null; // keep default 5-fret visuals
                }

                var trackPlayer = GetComponentInParent<TrackPlayer>();
                if (trackPlayer == null)
                    return null;

                var yargPlayer = trackPlayer.Player;
                if (yargPlayer == null)
                    return null;

                var profile = yargPlayer.Profile;
                if (profile == null)
                    return null;

                // Only alter visuals for five-fret guitar-type instruments
                switch (profile.CurrentInstrument)
                {
                    case Instrument.FiveFretGuitar:
                    case Instrument.FiveFretBass:
                    case Instrument.FiveFretRhythm:
                    case Instrument.FiveFretCoopGuitar:
                    case Instrument.Keys:
                        break;
                    default:
                        return null; // not a five-fret instrument; keep default
                }

                // Only modify for non-Expert difficulties
                if (profile.CurrentDifficulty == Difficulty.Expert)
                {
                    return null; // keep default 5-fret
                }

                // Easy/Medium/Hard -> return 4 frets visually
                return 4;
            }
            catch
            {
                // Fail-safe: don't change layout if anything goes wrong.
                return null;
            }
        }

        // Reposition the first 'count' frets evenly across the original left/right bounds
        // and hide the remaining ones. This only changes visuals (GameObject active/position),
        // internal arrays like _activeFrets remain sized to FretCount for compatibility.
        private void ApplyFretLayout(int count, bool leftyFlip)
        {
            if (_frets == null || _frets.Count == 0)
                return;

            count = Math.Min(count, _frets.Count);
            if (count == _frets.Count)
                return; // nothing to do

            // compute original left and right bounds from current local positions
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            for (int i = 0; i < _frets.Count; i++)
            {
                float x = _frets[i].transform.localPosition.x;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
            }

            if (Math.Abs(maxX - minX) < 1e-6f)
            {
                // Can't compute spacing — simply hide extras
                for (int i = 0; i < _frets.Count; i++)
                {
                    _frets[i].gameObject.SetActive(i < count);
                }
                // Build mapping to whatever positions we have
                _logicalToVisualX = new float[FretCount];
                for (int li = 0; li < FretCount; li++)
                {
                    _logicalToVisualX[li] = (li < count) ? _frets[Math.Min(li, count - 1)].transform.localPosition.x : _frets[count - 1].transform.localPosition.x;
                }

                // compute spacing & scaling conservatively
                float originalSpacing = (_frets.Count >= 2) ? (_trackWidth / _frets.Count) : 0f;
                float mapSpacing = (count >= 2) ? Math.Abs(_logicalToVisualX[1] - _logicalToVisualX[0]) : originalSpacing;
                _visualSpacing = mapSpacing;
                _usingVisualFretCount = true;

                // scale visible frets to fill gaps
                if (originalSpacing > 0f && mapSpacing > 0f)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var f = _frets[i];
                        float baseScale = (i < _initialFretScaleXs.Count) ? _initialFretScaleXs[i] : f.transform.localScale.x;
                        float desiredScaleX = baseScale * (mapSpacing / originalSpacing) * FretFillFactor;
                        var s = f.transform.localScale;
                        f.transform.localScale = new Vector3(desiredScaleX, s.y, s.z);
                    }
                }

                return;
            }

            float computedSpacing = (count == 1) ? 0f : (maxX - minX) / (count - 1);

            for (int i = 0; i < _frets.Count; i++)
            {
                if (i < count)
                {
                    float targetX = minX + computedSpacing * i;
                    Vector3 pos = _frets[i].transform.localPosition;
                    _frets[i].transform.localPosition = new Vector3(leftyFlip ? -targetX : targetX, pos.y, pos.z);
                    _frets[i].gameObject.SetActive(true);
                }
                else
                {
                    _frets[i].gameObject.SetActive(false);
                }
            }

            // Build logical->visual mapping
            _logicalToVisualX = new float[FretCount];
            for (int li = 0; li < FretCount; li++)
            {
                if (li < count)
                {
                    _logicalToVisualX[li] = _frets[li].transform.localPosition.x;
                }
                else
                {
                    // Map hidden logical indices to the closest visible fret (last visible)
                    _logicalToVisualX[li] = _frets[count - 1].transform.localPosition.x;
                }
            }

            if (count >= 2)
                _visualSpacing = Math.Abs(_logicalToVisualX[1] - _logicalToVisualX[0]);
            else
                _visualSpacing = 0f;

            // compute original spacing (based on prefab spawn formula)
            float originalSpacingPrefabs = (_frets.Count >= 2) ? (_trackWidth / _frets.Count) : 0f;

            // Scale visible frets to match new spacing (use FretFillFactor to avoid clips)
            if (originalSpacingPrefabs > 0f && _visualSpacing > 0f)
            {
                for (int i = 0; i < count; i++)
                {
                    var fret = _frets[i];
                    float baseScale = (i < _initialFretScaleXs.Count) ? _initialFretScaleXs[i] : fret.transform.localScale.x;
                    float desiredScaleX = baseScale * (_visualSpacing / originalSpacingPrefabs) * FretFillFactor;
                    var s = fret.transform.localScale;
                    fret.transform.localScale = new Vector3(desiredScaleX, s.y, s.z);
                }
            }

            _usingVisualFretCount = true;

            UpdatePadModeLaneSeparator(true);
        }

        // --------------- Public accessors for mapping (new) ----------------

        public bool IsUsingVisualFretCount() => _usingVisualFretCount;

        public float GetXForLogicalIndex(int logicalIndex)
        {
            if (_logicalToVisualX != null && logicalIndex >= 0 && logicalIndex < _logicalToVisualX.Length)
                return _logicalToVisualX[logicalIndex];

            if (_frets != null && logicalIndex >= 0 && logicalIndex < _frets.Count)
                return _frets[logicalIndex].transform.localPosition.x;

            return 0f;
        }

        public float GetVisualSpacing() => _visualSpacing;

        public float GetNoteFillFactor() => Mathf.Max(0f, NoteFillFactor);

        private void UpdatePadModeLaneSeparator(bool applied)
        {
            if (PadModeLaneSeparator == null)
                return;

            // use the same global flag check you already have in DetectDesiredFretCountFromProfile
            // (replace with your own flag if needed)
            bool isPro = YARG.Menu.MusicLibrary.MusicLibraryMenu.isProMode;

            // applied == true means we just applied the 4-lane visual mapping for non-Expert & non-Pro
            // Set X to 0 when mapping is applied; otherwise set to default -0.2f.
            float targetX = (!isPro && applied) ? 0f : -0.2f;

            var p = PadModeLaneSeparator.localPosition;
            if (Math.Abs(p.x - targetX) > 1e-6f)
            {
                PadModeLaneSeparator.localPosition = new Vector3(targetX, p.y, p.z);
            }
        }
    }
}