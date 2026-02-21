using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using YARG.Gameplay.Player;
using YARG.Helpers.Extensions;

namespace YARG.Gameplay.Visuals
{
    public abstract class TrackElement<TPlayer> : BaseElement
        where TPlayer : TrackPlayer
    {
        protected const float REMOVE_POINT = -4f;

        protected TPlayer Player { get; private set; }

        /// <summary>
        /// Whether or not the player has lefty flip on.
        /// </summary>
        protected bool LeftyFlip => Player.Player.Profile.LeftyFlip;

        /// <summary>
        /// The lefty flip position multiplier. <c>1</c> if lefty flip is off, <c>-1</c> if it is on.
        /// This is not automatically accounted for.
        /// </summary>
        protected float LeftyFlipMultiplier => LeftyFlip ? -1f : 1f;

        protected override void GameplayAwake()
        {
            Player = GetComponentInParent<TPlayer>();

            base.GameplayAwake();
        }

        private void Start()
        {
            // Get fade info
            float fadePos = Player.ZeroFadePosition;
            float fadeSize = Player.FadeSize;

            // Set all fade values for meshes
            var meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
            foreach (var meshRenderer in meshRenderers)
            {
                foreach (var material in meshRenderer.materials)
                {
                    material.SetFade(fadePos, fadeSize);
                }
            }

            // Set all fade values for note flares
            var noteFlares = GetComponentsInChildren<NoteFlare>(true);
            foreach (var noteFlare in noteFlares)
            {
                noteFlare.TrackPlayer = Player;
                noteFlare.SetFade(fadePos, fadeSize);
            }
        }

        protected override bool UpdateElementPosition()
        {
            // Calibration is not taken into consideration here, as that is instead handled in more
            // critical areas such as the game manager and players
            float z =
                TrackPlayer.STRIKE_LINE_POS                      // Shift origin to the strike line
                + (float) (ElementTime - GameManager.VisualTime) // Get time of note relative to now
                * Player.NoteSpeed;                              // Adjust speed (units/s)

            var cacheTransform = transform;
            cacheTransform.localPosition = cacheTransform.localPosition.WithZ(z);

            if (z < REMOVE_POINT - RemovePointOffset)
            {
                ParentPool.Return(this);
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static float GetElementX(int index, int subdivisions)
        {
            return TrackPlayer.TRACK_WIDTH / subdivisions * index - TrackPlayer.TRACK_WIDTH / 2f - 1f / subdivisions;
        }

        /// <summary>
        /// Mapping-aware element X. If a FretArray with visual mapping is present this returns
        /// the mapped X for the logical index (already including sign/lefty flip if the FretArray stored it).
        /// Otherwise falls back to the original static formula and includes LeftyFlipMultiplier.
        /// This helper prefers the common guitar 1-based indices: if index is in 1..FretCount it will be
        /// treated as 1-based and mapped to 0..FretCount-1. If not, it will attempt 0-based usage.
        /// </summary>
        protected float GetElementX_Mapped(int index, int subdivisions)
        {
            var fretArray = Player.GetComponentInChildren<FretArray>();
            if (fretArray != null && fretArray.IsUsingVisualFretCount())
            {
                int fc = fretArray.FretCount;

                // Prefer 1-based mapping when index is in 1..FretCount (typical for guitar enums)
                if (index > 0 && index <= fc)
                {
                    int zeroBased = index - 1;
                    return fretArray.GetXForLogicalIndex(zeroBased);
                }

                // Otherwise, accept a 0-based index if valid
                if (index >= 0 && index < fc)
                {
                    return fretArray.GetXForLogicalIndex(index);
                }

                // Out-of-range: clamp to nearest valid visual fret
                int clamped = Mathf.Clamp(index, 0, Math.Max(0, fc - 1));
                return fretArray.GetXForLogicalIndex(clamped);
            }

            // Fallback: original behavior (value times lefty multiplier)
            return GetElementX(index, subdivisions) * LeftyFlipMultiplier;
        }
    }
}