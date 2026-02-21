using System;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Song;
using YARG.Core.Utility;
using YARG.Helpers.Extensions;
using YARG.Menu.Persistent;
using YARG.Menu.MusicLibrary;
using YARG.Song;

namespace YARG.Menu.MusicLibrary
{
    public class Sidebar : MonoBehaviour
    {
        [SerializeField]
        private Transform _difficultyRingsTopContainer;
        [SerializeField]
        private Transform _difficultyRingsBottomContainer;

        [SerializeField]
        private TextMeshProUGUI _source;
        [SerializeField]
        private TextMeshProUGUI _charter;
        [SerializeField]
        private TextMeshProUGUI _genre;
        [SerializeField]
        private TextMeshProUGUI _year;
        [SerializeField]
        private TextMeshProUGUI _length;
        [SerializeField]
        private RawImage _albumCover;

        [FormerlySerializedAs("difficultyRingPrefab")]
        [Space]
        [SerializeField]
        private GameObject _difficultyRingPrefab;

        private readonly List<DifficultyRing> _difficultyRings = new();
        private CancellationTokenSource _cancellationToken;
        private ViewType _currentView;

        private MusicLibraryMenu _musicLibraryMenu;
        private SongSearchingField _songSearchingField;

        public void Initialize(MusicLibraryMenu musicLibraryMenu, SongSearchingField songSearchingField)
        {
            _musicLibraryMenu = musicLibraryMenu;
            _songSearchingField = songSearchingField;

            for (int i = 0; i < 5; ++i)
            {
                var go = Instantiate(_difficultyRingPrefab, _difficultyRingsTopContainer);
                _difficultyRings.Add(go.GetComponent<DifficultyRing>());
            }

            for (int i = 0; i < 5; ++i)
            {
                var go = Instantiate(_difficultyRingPrefab, _difficultyRingsBottomContainer);
                _difficultyRings.Add(go.GetComponent<DifficultyRing>());
            }
        }

        public void UpdateSidebar()
        {
            if (_musicLibraryMenu.ViewList.Count <= 0)
            {
                return;
            }

            var viewType = _musicLibraryMenu.CurrentSelection;
            if (_currentView != null && _currentView == viewType)
                return;

            _currentView = viewType;

            // Cancel album art
            if (_cancellationToken != null)
            {
                _cancellationToken.Cancel();
                _cancellationToken.Dispose();
                _cancellationToken = null;
            }

            switch (viewType)
            {
                case SongViewType songViewType:
                    ShowSongInfo(songViewType);
                    break;
                case CategoryViewType categoryViewType:
                    ClearSidebar();
                    ShowCategoryInfo(categoryViewType);
                    break;
                default:
                    ClearSidebar();
                    break;
            }
        }

        private void ShowCategoryInfo(CategoryViewType categoryViewType)
        {
            _source.text = categoryViewType.SourceCountText;
            _charter.text = categoryViewType.CharterCountText;
            _genre.text = categoryViewType.GenreCountText;
        }

        private void ClearSidebar()
        {
            // Hide album art
            _albumCover.texture = null;
            _albumCover.color = Color.clear;

            _year.text = string.Empty;
            _length.text = string.Empty;

            _source.text = string.Empty;
            _charter.text = string.Empty;
            _genre.text = string.Empty;

            // Hide all difficulty rings
            foreach (var difficultyRing in _difficultyRings)
            {
                difficultyRing.gameObject.SetActive(false);
            }
        }

        private void ShowSongInfo(SongViewType songViewType)
        {
            var songEntry = songViewType.SongEntry;

            _source.text = SongSources.SourceToGameName(songEntry.Source);
            _charter.text = songEntry.Charter;
            _genre.text = songEntry.Genre;
            _year.text = songEntry.ParsedYear;

            // Format and show length
            var time = TimeSpan.FromMilliseconds(songEntry.SongLengthMilliseconds);
            if (time.Hours > 0)
            {
                _length.text = time.ToString(@"h\:mm\:ss");
            }
            else
            {
                _length.text = time.ToString(@"m\:ss");
            }

            UpdateDifficulties(songEntry);

            _cancellationToken = new();
            _albumCover.LoadAlbumCover(songEntry, _cancellationToken.Token);
        }

        private void UpdateDifficulties(SongEntry entry)
        {
            // Show all difficulty rings
            foreach (var difficultyRing in _difficultyRings)
            {
                difficultyRing.gameObject.SetActive(true);
            }

            /*

                Vocals ; Bass/Pro Bass ; Lead/Pro Lead ; Drums/Pro Drums ; None
                Bottom Row is Blank

            */

            if (MusicLibraryMenu.isProMode == false)
            {
                _difficultyRings[0].SetInfo("keys", Instrument.Keys, entry[Instrument.Keys]);
                _difficultyRings[1].SetInfo("bass", Instrument.FiveFretBass, entry[Instrument.FiveFretBass]);
                _difficultyRings[2].SetInfo("guitar", Instrument.FiveFretGuitar, entry[Instrument.FiveFretGuitar]);
                _difficultyRings[3].SetInfo("rhythm", Instrument.FiveFretRhythm, entry[Instrument.FiveFretRhythm]);
            }
            else
            {
                _difficultyRings[0].SetInfo("realGuitar", Instrument.FiveFretGuitar, entry[Instrument.FiveFretGuitar]);
                _difficultyRings[1].SetInfo("realBass", Instrument.FiveFretBass, entry[Instrument.FiveFretBass]);
                _difficultyRings[2].SetInfo("realDrums", Instrument.FourLaneDrums, entry[Instrument.FourLaneDrums]);
                _difficultyRings[3].SetInfo("guitarCoop", Instrument.FiveFretCoopGuitar, entry[Instrument.FiveFretCoopGuitar]);
            }

            _difficultyRings[4].SetInfo("guitarCoop", Instrument.FiveFretCoopGuitar, entry[Instrument.FiveFretCoopGuitar]);
            _difficultyRings[5].SetInfo("guitarCoop", Instrument.FiveFretCoopGuitar, entry[Instrument.FiveFretCoopGuitar]);
            _difficultyRings[6].SetInfo("guitarCoop", Instrument.FiveFretCoopGuitar, entry[Instrument.FiveFretCoopGuitar]);
            _difficultyRings[7].SetInfo("guitarCoop", Instrument.FiveFretCoopGuitar, entry[Instrument.FiveFretCoopGuitar]);
            _difficultyRings[8].SetInfo("guitarCoop", Instrument.FiveFretCoopGuitar, entry[Instrument.FiveFretCoopGuitar]);
            _difficultyRings[9].SetInfo("guitarCoop", Instrument.FiveFretCoopGuitar, entry[Instrument.FiveFretCoopGuitar]);
        }

        public void PrimaryButtonClick()
        {
            _musicLibraryMenu.CurrentSelection.PrimaryButtonClick();
        }

        public void SearchFilter(string type)
        {
            var viewType = _musicLibraryMenu.CurrentSelection;

            if (viewType is not SongViewType songViewType)
            {
                return;
            }

            var songEntry = songViewType.SongEntry;

            switch (type)
            {
                case "source":
                    _songSearchingField.SetSearchInput(SortAttribute.Source, $"\"{songEntry.Source.SearchStr}\"");
                    break;
                case "album":
                    _songSearchingField.SetSearchInput(SortAttribute.Album, $"\"{songEntry.Album.SearchStr}\"");
                    break;
                case "year":
                    _songSearchingField.SetSearchInput(SortAttribute.Year, $"\"{songEntry.ParsedYear}\"");
                    break;
                case "charter":
                    _songSearchingField.SetSearchInput(SortAttribute.Charter, $"\"{songEntry.Charter.SearchStr}\"");
                    break;
                case "genre":
                    _songSearchingField.SetSearchInput(SortAttribute.Genre, $"\"{songEntry.Genre.SearchStr}\"");
                    break;
            }
        }
    }
}