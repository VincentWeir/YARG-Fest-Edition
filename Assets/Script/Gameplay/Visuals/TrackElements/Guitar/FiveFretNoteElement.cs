using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YARG.Core.Chart;
using YARG.Gameplay.Player;
using YARG.Helpers.Extensions;
using YARG.Menu.MusicLibrary;
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

        // Keep a handle to the running fade coroutine so we can stop it when the sustain end is destroyed
        private Coroutine _sustainFadeCoroutine;

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
                if (len <= maxSustainLength + sustainThresholdTolerance && sustainEndPrefab != null && sustainEndInstance == null && MusicLibraryMenu.isProMode == false)
                {
                    sustainEndInstance = Instantiate(sustainEndPrefab, transform);
                    sustainEndInstance.transform.localPosition = new Vector3(0f, 0f, len);

                    _sustainLine._lineRenderer.enabled = false;

                    // Start the fade-in coroutine for the URP material on the sustain end instance
                    // Stop any previous coroutine (shouldn't be one, but defensive)
                    if (_sustainFadeCoroutine != null)
                    {
                        StopCoroutine(_sustainFadeCoroutine);
                        _sustainFadeCoroutine = null;
                    }

                    _sustainFadeCoroutine = StartCoroutine(FadeInSustainEndMaterial(sustainEndInstance, 1f));
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
                // Stop the fade coroutine if it's running, then destroy
                if (_sustainFadeCoroutine != null)
                {
                    StopCoroutine(_sustainFadeCoroutine);
                    _sustainFadeCoroutine = null;
                }

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
                // Stop fade coroutine if running
                if (_sustainFadeCoroutine != null)
                {
                    StopCoroutine(_sustainFadeCoroutine);
                    _sustainFadeCoroutine = null;
                }

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

        /// <summary>
        /// Coroutine that fades the alpha of the first Renderer material it finds on the sustain end
        /// instance from 0 to 1 over duration seconds. It attempts to handle URP Lit materials by
        /// trying common color property names ("_BaseColor" then "_Color"). The material instance is
        /// accessed via renderer.material so the shared material is not modified.
        /// 
        /// Note: For the alpha change to be visible the material must be using a Surface Type that
        /// supports transparency (e.g. Transparent). If the prefab's material is Opaque you will need
        /// to either make a transparent variant or switch the material's surface type to Transparent.
        /// </summary>
        private IEnumerator FadeInSustainEndMaterial(GameObject instance, float duration)
        {
            if (instance == null)
                yield break;

            // Find a renderer (child or on the root)
            var rend = instance.GetComponentInChildren<Renderer>();
            if (rend == null)
                yield break;

            // Use renderer.material to create an instance (so we don't modify sharedMaterial)
            var mat = rend.material;
            if (mat == null)
                yield break;

            // Determine which color property to use for URP/standard materials
            string colorProp = null;
            if (mat.HasProperty("_BaseColor"))
                colorProp = "_BaseColor";
            else if (mat.HasProperty("_Color"))
                colorProp = "_Color";

            if (colorProp == null)
                yield break;

            // Ensure the material's surface type supports alpha (URP uses _Surface: 0=Opaque,1=Transparent)
            // This is best handled by preparing the prefab material in the editor, but we do a best-effort attempt here.
            if (mat.HasProperty("_Surface"))
            {
                // Set to Transparent (1) so alpha will be respected.
                // Note: changing this at runtime may not update shader keywords in some Unity versions.
                mat.SetFloat("_Surface", 1f);
            }

            // Read starting color and set alpha to 0 immediately
            Color col = mat.GetColor(colorProp);
            col.a = 0f;
            mat.SetColor(colorProp, col);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                col.a = t;
                mat.SetColor(colorProp, col);
                yield return null;
            }

            // Ensure final alpha is exactly 1
            col.a = 1f;
            mat.SetColor(colorProp, col);

            // Clear stored coroutine handle
            _sustainFadeCoroutine = null;
        }
    }
}