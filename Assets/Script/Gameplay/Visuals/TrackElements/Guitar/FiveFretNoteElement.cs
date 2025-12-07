using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YARG.Core.Chart;
using YARG.Gameplay.Player;
using YARG.Helpers.Extensions;
using YARG.Themes;

namespace YARG.Gameplay.Visuals
{
    public sealed class FiveFretNoteElement : NoteElement<GuitarNote, FiveFretPlayer>
    {
        private enum NoteType
        {
            Strum    = 0,
            HOPO     = 1,
            Tap      = 2,
            Open     = 3,
            OpenHOPO = 4,

            Count
        }

        [Space]
        [SerializeField]
        private SustainLine _normalSustainLine;
        [SerializeField]
        private SustainLine _openSustainLine;
        [SerializeField]
        private GameObject sustainEndPrefab;
        private GameObject sustainEndInstance;

        private SustainLine _sustainLine;

        [SerializeField]
        private float maxSustainLength;

        // Dynamically updates maxSustainLength using the equation Y = 180 / BPM, and clamps Y to 1
        private void UpdateMaxSustainLengthWithTempo()
        {
            // Fetch the tempo at the note's tick via YARG.Core API
            // GameManager.Chart.SyncTrack.Tempos is a List<TempoChange> (or similar).
            // Find the most recent tempo whose Tick <= NoteRef.Tick in a safe way.

            var tempos = GameManager.Chart.SyncTrack?.Tempos;
            float bpm = 120f; // fallback default bpm

            if (tempos != null && tempos.Count > 0)
            {
                // Walk backwards to find the previous tempo change at or before this tick.
                for (int i = tempos.Count - 1; i >= 0; i--)
                {
                    var t = tempos[i];
                    if (t.Tick <= NoteRef.Tick)
                    {
                        // BeatsPerMinute is a double; cast to float to avoid CS0266
                        bpm = (float)t.BeatsPerMinute; // adjust property name if necessary
                        break;
                    }
                }

                // If we didn't break (all tempos are after this note), use the first tempo entry.
                // This can happen for very small note ticks (before first tempo change).
                if (tempos[0].Tick > NoteRef.Tick)
                {
                    bpm = (float)tempos[0].BeatsPerMinute;
                }
            }

            float x = 30f * Player.NoteSpeed;
            float y = x / bpm;
            // Clamp to maximum 1f (and ensure non-negative)
            y = Mathf.Clamp(y, 0f, 1f);
            maxSustainLength = y;
        }

        // Make sure the remove it later if it has a sustain
        protected override float RemovePointOffset => (float) NoteRef.TimeLength * Player.NoteSpeed;

        public override void SetThemeModels(
            Dictionary<ThemeNoteType, GameObject> models,
            Dictionary<ThemeNoteType, GameObject> starPowerModels)
        {
            CreateNoteGroupArrays((int) NoteType.Count);

            AssignNoteGroup(models, starPowerModels, (int) NoteType.Strum,    ThemeNoteType.Normal);
            AssignNoteGroup(models, starPowerModels, (int) NoteType.HOPO,     ThemeNoteType.HOPO);
            AssignNoteGroup(models, starPowerModels, (int) NoteType.Tap,      ThemeNoteType.Tap);
            AssignNoteGroup(models, starPowerModels, (int) NoteType.Open,     ThemeNoteType.Open);
            AssignNoteGroup(models, starPowerModels, (int) NoteType.OpenHOPO, ThemeNoteType.OpenHOPO);
        }

        protected override void InitializeElement()
        {
            base.InitializeElement();

            var noteGroups = NoteRef.IsStarPower ? StarPowerNoteGroups : NoteGroups;

            if (NoteRef.Fret != (int) FiveFretGuitarFret.Open)
            {
                // Deal with non-open notes

                // Set the position
                transform.localPosition = new Vector3(GetElementX(NoteRef.Fret, 5), 0f, 0f) * LeftyFlipMultiplier;

                // Get which note model to use
                NoteGroup = NoteRef.Type switch
                {
                    GuitarNoteType.Strum => noteGroups[(int) NoteType.Strum],
                    GuitarNoteType.Hopo  => noteGroups[(int) NoteType.Tap],
                    GuitarNoteType.Tap   => noteGroups[(int) NoteType.Strum],
                    _ => throw new ArgumentOutOfRangeException(nameof(NoteRef.Type))
                };

                _sustainLine = _normalSustainLine;
            }
            else
            {
                // Deal with open notes

                // Set the position
                transform.localPosition = Vector3.zero;

                // Get which note model to use
                NoteGroup = NoteRef.Type switch
                {
                    GuitarNoteType.Strum => noteGroups[(int) NoteType.Open],
                    GuitarNoteType.Hopo or
                    GuitarNoteType.Tap   => noteGroups[(int) NoteType.OpenHOPO],
                    _ => throw new ArgumentOutOfRangeException(nameof(NoteRef.Type))
                };

                _sustainLine = _openSustainLine;
            }

            // Show and set material properties
            NoteGroup.SetActive(true);
            NoteGroup.Initialize();

            // Set line length
            if (NoteRef.IsSustain)
            {
                // --- Dynamic sustain length threshold using tempo ---
                UpdateMaxSustainLengthWithTempo();
                
                _sustainLine.gameObject.SetActive(true);

                float len = (float) NoteRef.TimeLength * Player.NoteSpeed;
                _sustainLine.Initialize(len);

                const float sustainThresholdTolerance = 0.05f;
                if (len <= maxSustainLength + sustainThresholdTolerance && sustainEndPrefab != null && sustainEndInstance == null)
                {
                    sustainEndInstance = Instantiate(sustainEndPrefab, transform);
                    sustainEndInstance.transform.localPosition = new Vector3(0f, 0f, len);

                    StartCoroutine(FadeIn(sustainEndInstance));

                    _sustainLine._lineRenderer.enabled = false;
                }
                else
                {
                    _sustainLine._lineRenderer.enabled = true;
                }
            }

            // Set note and sustain color
            UpdateColor();
        }

        public override void HitNote()
        {
            base.HitNote();

            if (!NoteRef.IsSustain)
            {
                ParentPool.Return(this);
            }
            else
            {
                HideNotes();
            }
        }

        public override void MissNote()
        {
            base.MissNote();

            if (sustainEndInstance != null)
            {
                Destroy(sustainEndInstance);
                sustainEndInstance = null;
            }

            if (NoteRef.IsSustain)
            {
                _sustainLine.gameObject.SetActive(false);
            }

            ParentPool.Return(this);
        }

        protected override void UpdateElement()
        {
            base.UpdateElement();

            UpdateSustain();
        }

        protected override void OnNoteStateChanged()
        {
            base.OnNoteStateChanged();

            UpdateColor();
        }

        public override void OnStarPowerUpdated()
        {
            base.OnStarPowerUpdated();

            UpdateColor();
        }

        private void UpdateSustain()
        {
            float adjustedSpeed = Player.NoteSpeed * GameManager.SongSpeed;

            if (_sustainLine.gameObject.activeSelf)
            {
                _sustainLine.UpdateSustainLine(adjustedSpeed);
            }

            // Move the sustain end object with the sustain line
            if (sustainEndInstance != null)
            {
                float len = (float) NoteRef.TimeLength * adjustedSpeed;
                sustainEndInstance.transform.localPosition = new Vector3(0f, 0f, len);
            }
        }

        private void UpdateColor()
        {
            var colors = Player.Player.ColorProfile.FiveFretGuitar;

            // Get which note color to use
            var colorNoStarPower = colors.GetNoteColor(NoteRef.Fret);
            var color = NoteRef.IsStarPower
                ? colors.GetNoteStarPowerColor(NoteRef.Fret)
                : colorNoStarPower;

            // Set the note color
            NoteGroup.SetColorWithEmission(color.ToUnityColor(), colorNoStarPower.ToUnityColor());

            // The rest of this method is for sustain only
            if (!NoteRef.IsSustain) return;

            _sustainLine.SetState(SustainState, color.ToUnityColor());
        }

        protected override void HideElement()
        {
            HideNotes();

            _normalSustainLine.gameObject.SetActive(false);
            _openSustainLine.gameObject.SetActive(false);
        }

        public override void SustainEnd(bool finished)
        {
            if (sustainEndInstance != null)
            {
                Destroy(sustainEndInstance);
                sustainEndInstance = null;
            }

            if (NoteRef.IsSustain)
            {
                _sustainLine.gameObject.SetActive(false);
            }

            if (finished)
            {
                ParentPool.Return(this);
            }
            else
            {
                HideNotes();
            }
        }

        public IEnumerator FadeIn(GameObject obj)
            {
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer == null) yield break;

                Material material = renderer.material;
                Color color = material.color;
        
                // Set initial alpha to 0 (fully transparent)
                color.a = 0f;
                material.color = color;

                // Fade in over time (1 second for example)
                float fadeDuration = 1.5f;
                float elapsedTime = 0f;

                while (elapsedTime < fadeDuration)
                {
                    elapsedTime += Time.deltaTime;
                    color.a = Mathf.Lerp(0f, 0.8f, elapsedTime / fadeDuration);
                    material.color = color;

                    yield return null;  // Wait for the next frame
                }

                // Ensure the alpha is set to 1 (fully visible) at the end
                color.a = 1f;
                material.color = color;
            }
    }
}